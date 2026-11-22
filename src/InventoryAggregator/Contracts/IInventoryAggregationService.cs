using InventoryAggregator.Models;

namespace InventoryAggregator.Contracts;

/// <summary>
/// Aggregates inventory records by category.
/// Has no knowledge of HTTP or the upstream API.
/// </summary>
public interface IInventoryAggregationService
{
    Task<AggregationResult> AggregateAsync(CancellationToken cancellationToken = default);
}
