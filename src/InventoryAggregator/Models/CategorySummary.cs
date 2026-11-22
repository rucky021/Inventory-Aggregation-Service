namespace InventoryAggregator.Models;

/// <summary>
/// Aggregated figures for a single product category.
/// Only records with <c>quantity &gt; 0</c> contribute to these figures.
/// </summary>
public sealed class CategorySummary
{
    /// <summary>The category name, e.g. <c>Electronics</c>.</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// Sum of <c>price × quantity</c> across all available records in this category.
    /// </summary>
    public decimal TotalInventoryValue { get; init; }

    /// <summary>Count of records with quantity greater than zero.</summary>
    public int AvailableRecordCount { get; init; }

    /// <summary>The record in this category with the most recent <c>lastUpdated</c> timestamp.</summary>
    public MostRecentRecord MostRecentlyUpdated { get; init; } = null!;

    /// <summary>
    /// The full set of records whose figures are reflected in <see cref="TotalInventoryValue"/>
    /// and <see cref="AvailableRecordCount"/>.
    /// </summary>
    public List<InventoryRecord> Items { get; init; } = new();
}
