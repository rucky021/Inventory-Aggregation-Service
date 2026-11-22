namespace InventoryAggregator.Models;

/// <summary>
/// Top-level response from <c>GET /api/aggregation</c>.
/// </summary>
public sealed class AggregationResult
{
    /// <summary>One entry per distinct category found in the inventory dataset.</summary>
    public List<CategorySummary> Categories { get; init; } = new();
}
