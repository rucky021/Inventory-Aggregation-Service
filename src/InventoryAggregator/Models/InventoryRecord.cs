namespace InventoryAggregator.Models;

/// <summary>
/// A single inventory record as returned by the upstream Inventory API.
/// </summary>
public sealed class InventoryRecord
{
    /// <summary>Unique identifier for the record.</summary>
    public int Id { get; init; }

    /// <summary>Product name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Category this record belongs to. Exactly one category per record.</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Unit price in USD.</summary>
    public decimal Price { get; init; }

    /// <summary>Units on hand. A value of <c>0</c> means the item is not available stock.</summary>
    public int Quantity { get; init; }

    /// <summary>ISO 8601 UTC timestamp of the last update.</summary>
    public DateTimeOffset LastUpdated { get; init; }
}
