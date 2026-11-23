using InventoryAggregator.Contracts;
using InventoryAggregator.Models;
using Microsoft.AspNetCore.Mvc;

namespace InventoryAggregator.Controllers;

/// <summary>
/// Provides inventory data — either aggregated by category or as a raw unfiltered list.
/// </summary>
[ApiController]
[Route("api/aggregation")]
[Produces("application/json")]
public sealed class AggregationController : ControllerBase
{
    private readonly IInventoryAggregationService _aggregationService;
    private readonly IInventoryApiClient _apiClient;
    private readonly ILogger<AggregationController> _logger;

    public AggregationController(
        IInventoryAggregationService aggregationService,
        IInventoryApiClient apiClient,
        ILogger<AggregationController> logger)
    {
        _aggregationService = aggregationService;
        _apiClient = apiClient;
        _logger = logger;
    }

    /// <summary>
    /// Returns all inventory records aggregated by category.
    /// </summary>
    /// <remarks>
    /// Fetches the complete dataset from the upstream Inventory API (all pages, concurrently),
    /// then applies the following rules:
    ///
    /// - Records with <c>quantity = 0</c> are excluded from all figures.
    /// - Remaining records are grouped by <c>category</c> (case-insensitive).
    /// - Per category: total inventory value (sum of price × quantity, rounded to 2 dp),
    ///   available record count, the most recently updated record, and the full list of
    ///   contributing records.
    ///
    /// Categories are returned in alphabetical order.
    /// </remarks>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    /// <returns>An aggregation result containing one summary per category.</returns>
    /// <response code="200">Aggregation succeeded. Returns the category summaries.</response>
    /// <response code="502">The upstream Inventory API was unreachable or returned an error.</response>
    /// <response code="500">An unexpected internal error occurred.</response>
    [HttpGet]
    [ProducesResponseType(typeof(AggregationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _aggregationService.AggregateAsync(cancellationToken);
            return Ok(result);
        }
        catch (InventoryApiException ex)
        {
            _logger.LogError(ex, "Upstream API failure during aggregation.");
            return StatusCode(502, new { error = "The upstream inventory API is unavailable or returned an error.", detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during aggregation.");
            return StatusCode(500, new { error = "An unexpected error occurred." });
        }
    }

    /// <summary>
    /// Returns every inventory record from the upstream API with no filtering or aggregation applied.
    /// </summary>
    /// <remarks>
    /// Useful for inspecting the raw dataset before aggregation rules are applied.
    /// Records with <c>quantity = 0</c> are included. No grouping or sorting is performed;
    /// records appear in the order they were received across all pages.
    /// </remarks>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    /// <returns>All records and a total count.</returns>
    /// <response code="200">Returns the full unfiltered record list.</response>
    /// <response code="502">The upstream Inventory API was unreachable or returned an error.</response>
    /// <response code="500">An unexpected internal error occurred.</response>
    /*[HttpGet("loadall")]
    [ProducesResponseType(typeof(RawInventoryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> LoadAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var records = await _apiClient.GetAllRecordsAsync(cancellationToken);
            return Ok(new RawInventoryResult
            {
                TotalCount = records.Count,
                Records = records.ToList(),
            });
        }
        catch (InventoryApiException ex)
        {
            _logger.LogError(ex, "Upstream API failure loading all records.");
            return StatusCode(502, new { error = "The upstream inventory API is unavailable or returned an error.", detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading all records.");
            return StatusCode(500, new { error = "An unexpected error occurred." });
        }
    }*/
}
