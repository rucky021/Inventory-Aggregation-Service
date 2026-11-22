namespace InventoryAggregator.Models;

public sealed class PagedResponse
{
    public List<InventoryRecord> Records { get; init; } = new();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
    public int TotalRecords { get; init; }
}
