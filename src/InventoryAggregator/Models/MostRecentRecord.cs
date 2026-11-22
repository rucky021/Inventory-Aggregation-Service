namespace InventoryAggregator.Models;

/// <summary>
/// A slim projection of the most recently updated record within a category.
/// </summary>
public sealed class MostRecentRecord
{
    /// <summary>Unique identifier of the record.</summary>
    public int Id { get; init; }

    /// <summary>Product name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>ISO 8601 UTC timestamp of the last update.</summary>
    public DateTimeOffset LastUpdated { get; init; }
}
