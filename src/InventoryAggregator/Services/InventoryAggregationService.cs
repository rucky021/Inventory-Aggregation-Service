using InventoryAggregator.Contracts;
using InventoryAggregator.Models;

namespace InventoryAggregator.Services;

/// <summary>
/// Pure aggregation logic. No HTTP awareness — it asks the client for records
/// and applies the business rules from the spec.
///
/// Rules applied:
///   1. Records with quantity == 0 are excluded.
///   2. Remaining records are grouped by category.
///   3. Per category: total inventory value (sum of price * quantity),
///      available record count, most-recently-updated record, and the
///      full list of records that contributed to the figures.
/// </summary>
public sealed class InventoryAggregationService : IInventoryAggregationService
{
    private readonly IInventoryApiClient _apiClient;

    public InventoryAggregationService(IInventoryApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<AggregationResult> AggregateAsync(CancellationToken cancellationToken = default)
    {
        var allRecords = await _apiClient.GetAllRecordsAsync(cancellationToken);

        var categories = allRecords
            .Where(r => r.Quantity > 0)                                        // Rule 1: exclude zero-quantity
            .GroupBy(r => r.Category.Trim(), StringComparer.OrdinalIgnoreCase) // Rule 2: case-insensitive grouping, whitespace-trimmed
            .Select(g => BuildSummary(g.First().Category.Trim(), g.ToList()))  // preserve original casing from first record
            .OrderBy(s => s.Category, StringComparer.OrdinalIgnoreCase)        // deterministic alphabetical order
            .ToList();

        return new AggregationResult { Categories = categories };
    }

    private static CategorySummary BuildSummary(string category, List<InventoryRecord> records)
    {
        var mostRecent = records.MaxBy(r => r.LastUpdated)!;

        return new CategorySummary
        {
            Category = category,
            TotalInventoryValue = Math.Round(records.Sum(r => r.Price * r.Quantity), 2, MidpointRounding.AwayFromZero), // Rule 3
            AvailableRecordCount = records.Count,                          // Rule 4
            MostRecentlyUpdated = new MostRecentRecord                     // Rule 5
            {
                Id = mostRecent.Id,
                Name = mostRecent.Name,
                LastUpdated = mostRecent.LastUpdated,
            },
            Items = records,                                               // Rule 6
        };
    }
}
