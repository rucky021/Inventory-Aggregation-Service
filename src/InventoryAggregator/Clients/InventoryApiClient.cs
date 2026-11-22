using System.Net.Http.Json;
using InventoryAggregator.Contracts;
using InventoryAggregator.Models;
using Microsoft.Extensions.Logging;

namespace InventoryAggregator.Clients;

/// <summary>
/// Fetches all inventory records from the upstream API.
///
/// Concurrency strategy:
///   1. Fetch page 1 to discover totalPages.
///   2. Fan out all remaining pages in parallel using Task.WhenAll so total
///      wall-clock time is roughly one round trip rather than N.
///   3. Merge results in page-number order so the returned list is stable.
///
/// Resilience: Polly retry + circuit-breaker policies are attached when the
/// named HttpClient is registered in Program.cs. This class handles per-page
/// response validation and translates every recoverable and unrecoverable
/// upstream failure into <see cref="InventoryApiException"/> so callers never
/// see an unhandled HttpRequestException or TaskCanceledException.
/// </summary>
public sealed class InventoryApiClient : IInventoryApiClient
{
    private const int PageSize = 25; // maximum allowed by the API

    private readonly HttpClient _httpClient;
    private readonly ILogger<InventoryApiClient> _logger;

    public InventoryApiClient(HttpClient httpClient, ILogger<InventoryApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<InventoryRecord>> GetAllRecordsAsync(
        CancellationToken cancellationToken = default)
    {
        // --- Step 1: fetch page 1 to learn total page count ---
        var firstPage = await FetchPageAsync(1, cancellationToken);
        ValidatePage(firstPage, expectedPage: 1);

        int totalPages = firstPage.TotalPages;
        _logger.LogInformation(
            "Inventory API reported {TotalPages} pages ({TotalRecords} records). " +
            "Fetching remaining pages concurrently.",
            totalPages, firstPage.TotalRecords);

        // --- Step 2: fan out remaining pages in parallel ---
        PagedResponse[] remainingPages = Array.Empty<PagedResponse>();
        if (totalPages > 1)
        {
            var tasks = Enumerable.Range(2, totalPages - 1)
                .Select(page => FetchPageAsync(page, cancellationToken))
                .ToArray();

            remainingPages = await Task.WhenAll(tasks);
        }

        // --- Step 3: validate and merge in order ---
        var allPages = new PagedResponse[totalPages];
        allPages[0] = firstPage;
        for (int i = 0; i < remainingPages.Length; i++)
        {
            ValidatePage(remainingPages[i], expectedPage: i + 2);
            allPages[i + 1] = remainingPages[i];
        }

        var allRecords = allPages.SelectMany(p => p.Records).ToList();
        _logger.LogInformation("Fetched {Count} records across {Pages} pages.", allRecords.Count, totalPages);

        return allRecords;
    }

    private async Task<PagedResponse> FetchPageAsync(int page, CancellationToken cancellationToken)
    {
        var url = $"api/records?page={page}&pageSize={PageSize}";

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(url, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InventoryApiException(
                $"Transport error fetching page {page}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InventoryApiException(
                $"Request timed out fetching page {page}.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            var body = await ReadBodySafelyAsync(response);
            _logger.LogWarning(
                "Upstream API returned {StatusCode} for page {Page}. Body: {Body}",
                status, page, body);
            throw new InventoryApiException(
                $"Upstream API returned {status} for page {page}.", status);
        }

        PagedResponse? result;
        try
        {
            result = await response.Content.ReadFromJsonAsync<PagedResponse>(
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InventoryApiException(
                $"Failed to deserialise response for page {page}.", ex);
        }

        if (result is null)
        {
            throw new InventoryApiException(
                $"Upstream API returned a null body for page {page}.");
        }

        return result;
    }

    private static void ValidatePage(PagedResponse page, int expectedPage)
    {
        if (page.Page != expectedPage)
        {
            throw new InventoryApiException(
                $"Page mismatch: requested page {expectedPage}, response contained page {page.Page}.");
        }

        if (page.TotalPages < 0 || page.TotalRecords < 0)
        {
            throw new InventoryApiException(
                $"Page {expectedPage} contains negative pagination metadata " +
                $"(totalPages={page.TotalPages}, totalRecords={page.TotalRecords}).");
        }

        if (page.Records is null)
        {
            throw new InventoryApiException(
                $"Page {expectedPage} has a null records array.");
        }
    }

    private static async Task<string> ReadBodySafelyAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadAsStringAsync();
        }
        catch
        {
            return "(unreadable)";
        }
    }
}
