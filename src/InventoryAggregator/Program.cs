using System.Reflection;
using InventoryAggregator.Clients;
using InventoryAggregator.Contracts;
using InventoryAggregator.Services;
using Microsoft.OpenApi.Models;
using Polly;
using Polly.Extensions.Http;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration -------------------------------------------------------
// Priority: environment variables → user secrets → appsettings.json
// Keys are NEVER committed to source control — appsettings.json is intentionally blank.
var apiBaseUrl = builder.Configuration["InventoryApi:BaseUrl"];
var apiKey     = builder.Configuration["InventoryApi:ApiKey"];

if (string.IsNullOrWhiteSpace(apiBaseUrl))
    throw new InvalidOperationException(
        "InventoryApi:BaseUrl is not configured. " +
        "Set it via: dotnet user-secrets set \"InventoryApi:BaseUrl\" \"<url>\"  " +
        "or the environment variable INVENTORYAPI__BASEURL.");

if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException(
        "InventoryApi:ApiKey is not configured. " +
        "Set it via: dotnet user-secrets set \"InventoryApi:ApiKey\" \"<key>\"  " +
        "or the environment variable INVENTORYAPI__APIKEY.");

// ---- Resilience policies -------------------------------------------------
// Retry handles two classes of failure:
//   - Transient HTTP errors (5xx, network faults) via HandleTransientHttpError()
//   - 429 Too Many Requests, which the upstream rate-limiter returns and which
//     HandleTransientHttpError() does NOT cover by default.
//
// Wait duration (per attempt):
//   - If the response carries a Retry-After header (seconds or HTTP-date), honour it.
//   - Otherwise fall back to exponential back-off: 2 s, 4 s, 8 s.
//
// Circuit breaker: opens for 30 s after 5 consecutive failures so we stop
// hammering an already-struggling upstream.

static TimeSpan GetRetryDelay(int attempt, DelegateResult<HttpResponseMessage> outcome)
{
    // Check for Retry-After header on 429 (and optionally 503) responses.
    var retryAfter = outcome.Result?.Headers.RetryAfter;
    if (retryAfter is not null)
    {
        if (retryAfter.Delta.HasValue)
            return retryAfter.Delta.Value;

        if (retryAfter.Date.HasValue)
        {
            var wait = retryAfter.Date.Value - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
                return wait;
        }
    }

    // Default: exponential back-off (2 s, 4 s, 8 s).
    return TimeSpan.FromSeconds(Math.Pow(2, attempt));
}

var retryPolicy = Policy<HttpResponseMessage>
    .Handle<HttpRequestException>()
    .OrResult(r => (int)r.StatusCode >= 500 || r.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
    .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: (attempt, outcome, _) => GetRetryDelay(attempt, outcome),
        onRetryAsync: (_, _, _, _) => Task.CompletedTask);

var circuitBreakerPolicy = HttpPolicyExtensions
    .HandleTransientHttpError()
    .OrResult(r => r.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
    .CircuitBreakerAsync(
        handledEventsAllowedBeforeBreaking: 5,
        durationOfBreak: TimeSpan.FromSeconds(30));

// ---- HttpClient via IHttpClientFactory -----------------------------------
builder.Services
    .AddHttpClient<IInventoryApiClient, InventoryApiClient>(client =>
    {
        client.BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + '/');
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .AddPolicyHandler(retryPolicy)
    .AddPolicyHandler(circuitBreakerPolicy);

// ---- Application services ------------------------------------------------
builder.Services.AddScoped<IInventoryAggregationService, InventoryAggregationService>();
builder.Services.AddControllers();

// ---- Swagger / OpenAPI ---------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "Inventory Aggregation API",
        Version     = "v1",
        Description = "Reads inventory records from the upstream Inventory API, " +
                      "aggregates them by category, and exposes the result over HTTP. " +
                      "Records with quantity = 0 are excluded from all figures.",
    });

    // Pull in XML doc comments from the assembly so controller summaries appear in the UI.
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);
});

var app = builder.Build();

// ---- Middleware ----------------------------------------------------------
// Serve Swagger JSON and UI in all environments.
// Restrict to Development only if the API becomes public-facing.
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Inventory Aggregation API v1");
    options.RoutePrefix = "swagger"; // UI at /swagger
    options.DocumentTitle = "Inventory Aggregation API";
    options.DefaultModelsExpandDepth(-1); // collapse schema section by default
});

app.UseHttpsRedirection();
app.MapControllers();

app.Run();

// Expose Program to the test project's WebApplicationFactory.
public partial class Program { }
