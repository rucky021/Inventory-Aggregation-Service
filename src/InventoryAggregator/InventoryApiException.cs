namespace InventoryAggregator;

/// <summary>
/// Thrown when the upstream Inventory API returns an unrecoverable error.
/// </summary>
public sealed class InventoryApiException : Exception
{
    public int? StatusCode { get; }

    public InventoryApiException(string message, int? statusCode = null)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public InventoryApiException(string message, Exception inner, int? statusCode = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
