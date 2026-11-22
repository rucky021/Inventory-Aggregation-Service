using FluentAssertions;
using InventoryAggregator.Contracts;
using InventoryAggregator.Controllers;
using InventoryAggregator.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace InventoryAggregator.Tests;

public sealed class AggregationControllerTests
{
    private readonly Mock<IInventoryAggregationService> _serviceMock = new();
    private readonly Mock<IInventoryApiClient> _clientMock = new();

    private AggregationController BuildController() =>
        new(_serviceMock.Object, _clientMock.Object, NullLogger<AggregationController>.Instance);

    // ------------------------------------------------------------------
    // GET /api/aggregation
    // ------------------------------------------------------------------

    [Fact]
    public async Task Get_Returns200_WithAggregationResult()
    {
        var expected = new AggregationResult
        {
            Categories = new List<CategorySummary>
            {
                new()
                {
                    Category = "Electronics",
                    TotalInventoryValue = 100m,
                    AvailableRecordCount = 1,
                    MostRecentlyUpdated = new MostRecentRecord { Id = 1, Name = "Widget", LastUpdated = DateTimeOffset.UtcNow },
                    Items = new List<InventoryRecord>()
                }
            }
        };

        _serviceMock.Setup(s => s.AggregateAsync(default)).ReturnsAsync(expected);

        var result = await BuildController().GetAsync(default) as OkObjectResult;

        result!.StatusCode.Should().Be(200);
        result.Value.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task Get_Returns502_WhenApiClientThrows()
    {
        _serviceMock.Setup(s => s.AggregateAsync(default))
            .ThrowsAsync(new InventoryApiException("upstream down", 503));

        var result = await BuildController().GetAsync(default) as ObjectResult;

        result!.StatusCode.Should().Be(502);
    }

    [Fact]
    public async Task Get_Returns500_WhenUnexpectedExceptionThrown()
    {
        _serviceMock.Setup(s => s.AggregateAsync(default))
            .ThrowsAsync(new InvalidOperationException("unexpected"));

        var result = await BuildController().GetAsync(default) as ObjectResult;

        result!.StatusCode.Should().Be(500);
    }

    // ------------------------------------------------------------------
    // GET /api/aggregation/loadall
    // ------------------------------------------------------------------

    [Fact]
    public async Task LoadAll_Returns200_WithAllRecordsUnfiltered()
    {
        var records = new List<InventoryRecord>
        {
            new() { Id = 1, Name = "A", Category = "X", Price = 1m, Quantity = 5,  LastUpdated = DateTimeOffset.UtcNow },
            new() { Id = 2, Name = "B", Category = "Y", Price = 2m, Quantity = 0,  LastUpdated = DateTimeOffset.UtcNow }, // zero-qty included
            new() { Id = 3, Name = "C", Category = "X", Price = 3m, Quantity = 10, LastUpdated = DateTimeOffset.UtcNow },
        };

        _clientMock.Setup(c => c.GetAllRecordsAsync(default)).ReturnsAsync(records);

        var result = await BuildController().LoadAllAsync(default) as OkObjectResult;
        var body = result!.Value as RawInventoryResult;

        result.StatusCode.Should().Be(200);
        body!.TotalCount.Should().Be(3);
        body.Records.Should().HaveCount(3);
    }

    [Fact]
    public async Task LoadAll_IncludesZeroQuantityRecords()
    {
        var records = new List<InventoryRecord>
        {
            new() { Id = 1, Name = "Available",    Category = "X", Price = 10m, Quantity = 1, LastUpdated = DateTimeOffset.UtcNow },
            new() { Id = 2, Name = "Out of stock", Category = "X", Price = 10m, Quantity = 0, LastUpdated = DateTimeOffset.UtcNow },
        };

        _clientMock.Setup(c => c.GetAllRecordsAsync(default)).ReturnsAsync(records);

        var result = await BuildController().LoadAllAsync(default) as OkObjectResult;
        var body = result!.Value as RawInventoryResult;

        // Both records must be present — no filtering applied
        body!.Records.Should().Contain(r => r.Quantity == 0);
        body.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task LoadAll_Returns502_WhenApiClientThrows()
    {
        _clientMock.Setup(c => c.GetAllRecordsAsync(default))
            .ThrowsAsync(new InventoryApiException("rate limited", 429));

        var result = await BuildController().LoadAllAsync(default) as ObjectResult;

        result!.StatusCode.Should().Be(502);
    }

    [Fact]
    public async Task LoadAll_Returns500_WhenUnexpectedExceptionThrown()
    {
        _clientMock.Setup(c => c.GetAllRecordsAsync(default))
            .ThrowsAsync(new InvalidOperationException("unexpected"));

        var result = await BuildController().LoadAllAsync(default) as ObjectResult;

        result!.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task LoadAll_TotalCount_MatchesRecordListLength()
    {
        var records = Enumerable.Range(1, 7)
            .Select(i => new InventoryRecord
            {
                Id = i, Name = $"Item {i}", Category = "Cat",
                Price = 1m, Quantity = i, LastUpdated = DateTimeOffset.UtcNow
            })
            .ToList();

        _clientMock.Setup(c => c.GetAllRecordsAsync(default)).ReturnsAsync(records);

        var result = await BuildController().LoadAllAsync(default) as OkObjectResult;
        var body = result!.Value as RawInventoryResult;

        body!.TotalCount.Should().Be(body.Records.Count);
    }
}
