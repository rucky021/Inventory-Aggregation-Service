using InventoryAggregator.Models;

namespace InventoryAggregator.Contracts;

/// <summary>
/// Retrieves inventory records from the upstream API.
/// Implementations own all HTTP concerns: pagination, retries, and error mapping.
/// </summary>
public interface IInventoryApiClient
{
    /// <summary>
    /// Returns every available record by walking all pages.
    /// Throws <see cref="InventoryApiException"/> on unrecoverable upstream failures.
    /// </summary>
    Task<IReadOnlyList<InventoryRecord>> GetAllRecordsAsync(CancellationToken cancellationToken = default);
}
