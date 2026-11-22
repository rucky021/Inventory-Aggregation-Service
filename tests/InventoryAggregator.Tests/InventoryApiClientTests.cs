using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using InventoryAggregator.Clients;
using InventoryAggregator.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Polly;

namespace InventoryAggregator.Tests;

/// <summary>
/// Tests for InventoryApiClient covering:
///   - happy-path single and multi-page fetching
///   - concurrent parallel fetch of remaining pages
///   - non-success HTTP responses
///   - transport errors (HttpRequestException)
///   - timeout errors
///   - null/corrupt response bodies
///   - pagination metadata validation
/// </summary>
public sealed class InventoryApiClientTests
{
    // ------------------------------------------------------------------
    // Happy path
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetAllRecords_SinglePage_ReturnsAllRecords()
    {
        var records = new[] { MakeRecord(1), MakeRecord(2) };
        var handler = SetupHandler(OkJson(BuildPage(page: 1, totalPages: 1, records: records)));
        var client = BuildClient(handler);

        var result = await client.GetAllRecordsAsync();

        result.Should().HaveCount(2);
        result.Select(r => r.Id).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public async Task GetAllRecords_MultiplePages_FetchesAllAndMergesInOrder()
    {
        var page1Records = new[] { MakeRecord(1), MakeRecord(2) };
        var page2Records = new[] { MakeRecord(3), MakeRecord(4) };

        var responses = new Queue<HttpResponseMessage>(new[]
        {
            OkJson(BuildPage(page: 1, totalPages: 2, records: page1Records)),
            OkJson(BuildPage(page: 2, totalPages: 2, records: page2Records)),
        });

        var handler = SetupQueuedHandler(responses);
        var client = BuildClient(handler);

        var result = await client.GetAllRecordsAsync();

        result.Should().HaveCount(4);
        // Verify order is preserved (page1 before page2)
        result[0].Id.Should().Be(1);
        result[2].Id.Should().Be(3);
    }

    [Fact]
    public async Task GetAllRecords_ConcurrentFetch_IssuesRequestsForAllPages()
    {
        // 5-page dataset: we expect exactly 5 HTTP calls total.
        int callCount = 0;
        var responses = Enumerable.Range(1, 5)
            .Select(p => OkJson(BuildPage(page: p, totalPages: 5, records: new[] { MakeRecord(p * 10) })))
            .ToArray();

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var idx = Interlocked.Increment(ref callCount) - 1;
                return responses[Math.Min(idx, responses.Length - 1)];
            });

        var client = BuildClient(handlerMock);

        var result = await client.GetAllRecordsAsync();

        callCount.Should().Be(5);
        result.Should().HaveCount(5);
    }

    // ------------------------------------------------------------------
    // 429 Too Many Requests
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetAllRecords_429Response_ThrowsInventoryApiException()
    {
        // The client itself throws on non-2xx — Polly retry is wired at the
        // HttpClient level in Program.cs, not inside InventoryApiClient.
        // This test confirms the client classifies 429 as a typed failure.
        var handler = SetupHandler(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var client = BuildClient(handler);

        await client.Invoking(c => c.GetAllRecordsAsync())
            .Should().ThrowAsync<InventoryApiException>()
            .WithMessage("*429*");
    }

    [Fact]
    public async Task GetAllRecords_429WithRetryAfterDelta_IncludesStatusCodeInException()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            TimeSpan.FromSeconds(5));

        var handler = SetupHandler(response);
        var client = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<InventoryApiException>(
            () => client.GetAllRecordsAsync());

        ex.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task GetAllRecords_429ThenSuccess_RetryPolicyRecovers()
    {
        // Simulate: first call returns 429, second returns a valid page.
        // Uses an inline DelegatingHandler that wraps a Polly retry policy
        // so we can verify retry behaviour without needing the full DI stack.
        var callCount = 0;
        var innerHandler = new Mock<HttpMessageHandler>();
        innerHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                return OkJson(BuildPage(page: 1, totalPages: 1, records: new[] { MakeRecord(1) }));
            });

        // Wire a Polly retry policy that handles 429 via a custom DelegatingHandler.
        var retryPolicy = Policy<HttpResponseMessage>
            .HandleResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(1, _ => TimeSpan.Zero);

        var pollyHandler = new PollyRetryHandler(retryPolicy, innerHandler.Object);
        var httpClient = new HttpClient(pollyHandler)
        {
            BaseAddress = new Uri("https://inventory.example.com/"),
        };
        var client = new InventoryApiClient(httpClient, NullLogger<InventoryApiClient>.Instance);

        var result = await client.GetAllRecordsAsync();

        callCount.Should().Be(2);   // one 429, one success
        result.Should().HaveCount(1);
    }



    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task GetAllRecords_NonSuccessStatus_ThrowsInventoryApiException(HttpStatusCode code)
    {
        var handler = SetupHandler(new HttpResponseMessage(code));
        var client = BuildClient(handler);

        await client.Invoking(c => c.GetAllRecordsAsync())
            .Should().ThrowAsync<InventoryApiException>()
            .WithMessage($"*{(int)code}*");
    }

    // ------------------------------------------------------------------
    // Transport failures
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetAllRecords_TransportError_ThrowsInventoryApiException()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var client = BuildClient(handlerMock);

        await client.Invoking(c => c.GetAllRecordsAsync())
            .Should().ThrowAsync<InventoryApiException>()
            .WithMessage("*Transport error*");
    }

    // ------------------------------------------------------------------
    // Null / corrupt body
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetAllRecords_NullBody_ThrowsInventoryApiException()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json"),
        };
        var handler = SetupHandler(response);
        var client = BuildClient(handler);

        await client.Invoking(c => c.GetAllRecordsAsync())
            .Should().ThrowAsync<InventoryApiException>()
            .WithMessage("*null body*");
    }

    [Fact]
    public async Task GetAllRecords_MalformedJson_ThrowsInventoryApiException()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json", System.Text.Encoding.UTF8, "application/json"),
        };
        var handler = SetupHandler(response);
        var client = BuildClient(handler);

        await client.Invoking(c => c.GetAllRecordsAsync())
            .Should().ThrowAsync<InventoryApiException>()
            .WithMessage("*deseriali*");
    }

    // ------------------------------------------------------------------
    // Pagination metadata validation
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetAllRecords_PageNumberMismatch_ThrowsInventoryApiException()
    {
        // Response claims to be page 2 but we asked for page 1.
        var handler = SetupHandler(OkJson(BuildPage(page: 2, totalPages: 1)));
        var client = BuildClient(handler);

        await client.Invoking(c => c.GetAllRecordsAsync())
            .Should().ThrowAsync<InventoryApiException>()
            .WithMessage("*mismatch*");
    }

    [Fact]
    public async Task GetAllRecords_NegativeTotalPages_ThrowsInventoryApiException()
    {
        var handler = SetupHandler(OkJson(BuildPage(page: 1, totalPages: -1)));
        var client = BuildClient(handler);

        await client.Invoking(c => c.GetAllRecordsAsync())
            .Should().ThrowAsync<InventoryApiException>()
            .WithMessage("*negative pagination*");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Mock<HttpMessageHandler> SetupHandler(HttpResponseMessage response)
    {
        var mock = new Mock<HttpMessageHandler>();
        mock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
        return mock;
    }

    private static Mock<HttpMessageHandler> SetupQueuedHandler(Queue<HttpResponseMessage> responses)
    {
        var mock = new Mock<HttpMessageHandler>();
        mock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => responses.Dequeue());
        return mock;
    }

    private static InventoryApiClient BuildClient(Mock<HttpMessageHandler> handlerMock)
    {
        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://inventory.example.com/"),
        };
        return new InventoryApiClient(httpClient, NullLogger<InventoryApiClient>.Instance);
    }

    private static HttpResponseMessage OkJson(object payload) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload),
        };

    private static PagedResponse BuildPage(
        int page,
        int totalPages,
        InventoryRecord[]? records = null) =>
        new()
        {
            Page = page,
            PageSize = 25,
            TotalPages = totalPages,
            TotalRecords = totalPages * 25,
            Records = (records ?? Array.Empty<InventoryRecord>()).ToList(),
        };

    private static InventoryRecord MakeRecord(int id) =>
        new()
        {
            Id = id,
            Name = $"Item {id}",
            Category = "Electronics",
            Price = 10m,
            Quantity = 1,
            LastUpdated = DateTimeOffset.UtcNow,
        };
}

/// <summary>
/// A minimal DelegatingHandler that applies a Polly policy, used in tests
/// to verify retry behaviour without the full ASP.NET Core DI stack.
/// </summary>
internal sealed class PollyRetryHandler : DelegatingHandler
{
    private readonly IAsyncPolicy<HttpResponseMessage> _policy;

    public PollyRetryHandler(IAsyncPolicy<HttpResponseMessage> policy, HttpMessageHandler inner)
        : base(inner)
    {
        _policy = policy;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        _policy.ExecuteAsync(ct => base.SendAsync(request, ct), cancellationToken);
}
