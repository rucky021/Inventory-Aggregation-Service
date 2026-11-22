namespace InventoryAggregator.Models;

/// <summary>
/// Response envelope for <c>GET /api/aggregation/loadall</c>.
/// Contains every record returned by the upstream API with no filtering applied.
/// </summary>
public sealed class RawInventoryResult
{
    /// <summary>Total number of records returned.</summary>
    public int TotalCount { get; init; }

    /// <summary>All inventory records in the order they were received from the upstream API.</summary>
    public List<InventoryRecord> Records { get; init; } = new();
}
