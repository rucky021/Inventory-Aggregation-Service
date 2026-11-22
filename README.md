# Inventory Aggregation Service

A .NET 11 Web API that reads inventory records from an upstream Inventory API, aggregates them by category, and exposes the results through two HTTP endpoints — one for aggregated data and one for the raw unfiltered dataset.

---

## Project Details

| Item | Value |
|------|-------|
| Language | C# 13 |
| Framework | ASP.NET Core (.NET 11) |
| SDK | .NET SDK 11.0.100 |
| Test framework | xUnit 2.9 |
| Resilience | Polly (retry + circuit breaker, 429-aware) |
| API documentation | Swagger UI via Swashbuckle 6.9 |

---

## Project Structure

```
inventory/
├── ARCHITECTURE.md                  ← full architecture and decisions reference
├── ARCHITECTURE_NOTE.md             ← half-page architecture summary (submission)
│
└── project/
    ├── InventoryAggregator.slnx     ← solution file
    ├── README.md                    ← this file
    ├── ARCHITECTURE_NOTE.md
    │
    ├── src/
    │   └── InventoryAggregator/     ← ASP.NET Core Web API
    │       ├── Controllers/
    │       │   └── AggregationController.cs   ← GET /api/aggregation
    │       │                                     GET /api/aggregation/loadall
    │       ├── Clients/
    │       │   └── InventoryApiClient.cs      ← HTTP + pagination + resilience
    │       ├── Services/
    │       │   └── InventoryAggregationService.cs  ← pure aggregation logic
    │       ├── Contracts/
    │       │   ├── IInventoryApiClient.cs
    │       │   └── IInventoryAggregationService.cs
    │       ├── Models/
    │       │   ├── InventoryRecord.cs         ← upstream record shape
    │       │   ├── PagedResponse.cs           ← upstream paged envelope
    │       │   ├── CategorySummary.cs         ← per-category aggregated output
    │       │   ├── AggregationResult.cs       ← top-level aggregation response
    │       │   ├── MostRecentRecord.cs        ← slim most-recent projection
    │       │   └── RawInventoryResult.cs      ← loadall response envelope
    │       ├── InventoryApiException.cs       ← typed upstream failure
    │       ├── Program.cs                     ← DI, Polly, Swagger wiring
    │       └── appsettings.json
    │
    └── tests/
        └── InventoryAggregator.Tests/
            ├── AggregationControllerTests.cs  ←  8 tests (controller layer)
            ├── InventoryAggregationServiceTests.cs  ← 17 tests (business logic)
            └── InventoryApiClientTests.cs     ← 11 tests (HTTP + resilience)
```

---

## Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/api/aggregation` | Records grouped by category, zero-quantity excluded, totals computed |
| `GET` | `/api/aggregation/loadall` | Every record from the upstream API, unfiltered and ungrouped |
| — | `/swagger` | Swagger UI — interactive documentation for both endpoints |

---

## Run Locally

### 1. Configure the API base URL and key

The app requires two values before it will start. Choose one option.

#### Option 1 — User Secrets *(recommended)*

```powershell
cd C:\Users\chenh\Desktop\inventory\project\src\InventoryAggregator

dotnet user-secrets init
dotnet user-secrets set "InventoryApi:BaseUrl" "https://localhost:7001"
dotnet user-secrets set "InventoryApi:ApiKey"  "<your-api-key>"

# Verify
dotnet user-secrets list
```

#### Option 2 — Environment Variables

```powershell
# PowerShell (current session only)
$env:INVENTORYAPI__BASEURL = "https://localhost:7001"
$env:INVENTORYAPI__APIKEY  = "<your-api-key>"
```

```bash
# bash / macOS / Linux
export INVENTORYAPI__BASEURL="https://localhost:7001"
export INVENTORYAPI__APIKEY="<your-api-key>"
```

> The double underscore `__` maps to the `:` separator in nested .NET config keys.

#### Option 3 — appsettings.json

Edit `src/InventoryAggregator/appsettings.json`:

```json
{
  "InventoryApi": {
    "BaseUrl": "https://localhost:7001",
    "ApiKey": "<your-api-key>"
  }
}
```

> ⚠️ Do not commit a real API key to source control.

#### How to find the correct Base URL

Open the upstream Inventory API project and check `Properties/launchSettings.json`:

```json
{ "applicationUrl": "https://localhost:7001;http://localhost:5001" }
```

Use the `https://` value as `InventoryApi:BaseUrl`.

---

### 2. Trust the HTTPS development certificate *(first run only)*

```powershell
dotnet dev-certs https --trust
```

---

### 3. Start the app

```powershell
cd C:\Users\chenh\Desktop\inventory\project\src\InventoryAggregator
dotnet run
```

The console prints the port:

```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:7142
```

---

### 4. Use the endpoints

**Swagger UI** — open in a browser, expand an endpoint, click **Try it out**, then **Execute**:

```
https://localhost:7142/swagger
```

**Aggregated data** (filtered, grouped by category):

```powershell
Invoke-RestMethod -Uri "https://localhost:7142/api/aggregation" -SkipCertificateCheck
```

**Raw unfiltered data** (every record, no grouping):

```powershell
Invoke-RestMethod -Uri "https://localhost:7142/api/aggregation/loadall" -SkipCertificateCheck
```

---

### 5. Run the tests

```powershell
cd C:\Users\chenh\Desktop\inventory\project
dotnet test
```

Expected: **36 tests, 0 failures.**

```powershell
# Verbose output
dotnet test --logger "console;verbosity=normal"

# Single class
dotnet test --filter "FullyQualifiedName~AggregationControllerTests"
dotnet test --filter "FullyQualifiedName~InventoryAggregationServiceTests"
dotnet test --filter "FullyQualifiedName~InventoryApiClientTests"
```

---

## Deploy

### Self-contained executable (no .NET runtime needed on target)

```powershell
cd C:\Users\chenh\Desktop\inventory\project\src\InventoryAggregator

dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -o C:\publish\InventoryAggregator
```

Run on the target machine (set env vars first):

```powershell
$env:INVENTORYAPI__BASEURL = "https://your-api-url/"
$env:INVENTORYAPI__APIKEY  = "your-api-key"
C:\publish\InventoryAggregator\InventoryAggregator.exe
```

Change `-r win-x64` to `linux-x64` or `osx-x64` for other platforms.

---

### Framework-dependent publish (smaller output, requires .NET 11 runtime on target)

```powershell
dotnet publish -c Release -o C:\publish\InventoryAggregator
dotnet C:\publish\InventoryAggregator\InventoryAggregator.dll
```

---

### Docker

Create a `Dockerfile` at the solution root:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/InventoryAggregator/InventoryAggregator.csproj \
    -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "InventoryAggregator.dll"]
```

Build and run:

```bash
docker build -t inventory-aggregator .

docker run -p 8080:8080 \
  -e INVENTORYAPI__BASEURL="https://your-api-url/" \
  -e INVENTORYAPI__APIKEY="your-api-key" \
  inventory-aggregator
```

Access: `http://localhost:8080/api/aggregation` and `http://localhost:8080/swagger`

---

### Change the listen port

```powershell
# Environment variable
$env:ASPNETCORE_URLS = "http://0.0.0.0:8080"
dotnet run

# Command-line argument
dotnet run --urls "http://0.0.0.0:8080"
```

---

## CI/CD Pipeline (GitHub Actions)

The pipeline is defined in `.github/workflows/ci-cd.yml`. Full setup instructions are in `GITHUB_ACTIONS_SETUP.md`.

### 3-job pipeline

```
┌─────────┐     ┌──────┐     ┌──────────────────────────────────────────┐
│  Build  │────▶│ Test │────▶│  Deploy  (main branch push only)         │
└─────────┘     └──────┘     │  ├─ Zip deploy → Azure App Service       │
                              │  └─ Docker build + push → ACR            │
                              └──────────────────────────────────────────┘
```

| Job | Runner | What happens |
|-----|--------|-------------|
| **1 · Build** | `windows-latest` | Checkout → Install .NET 11 → Restore → Compile → Cache build output |
| **2 · Test** | `windows-latest` | Restore cache → Run 36 tests → Publish TRX results → Upload coverage artifact |
| **3 · Deploy** | `windows-latest` | `dotnet publish` → deploy to Azure App Service → set App Settings → Docker build & push to ACR |

### Trigger rules

| Event | Build | Test | Deploy |
|-------|-------|------|--------|
| Push to `main` | ✓ | ✓ | ✓ |
| Push to `develop` | ✓ | ✓ | — |
| Pull request → `main` | ✓ | ✓ | — |

### Required GitHub Secrets

Go to **Settings → Secrets and variables → Actions** and add:

| Secret | Description |
|--------|-------------|
| `AZURE_CREDENTIALS` | Service principal JSON (`az ad sp create-for-rbac --sdk-auth`) |
| `AZURE_WEBAPP_PUBLISH_PROFILE` | Publish profile XML downloaded from Azure Portal |
| `AZURE_WEBAPP_NAME` | App Service name |
| `AZURE_RESOURCE_GROUP` | Resource group name |
| `INVENTORYAPI_BASEURL` | Upstream API base URL |
| `INVENTORYAPI_APIKEY` | Upstream API key |
| `ACR_LOGIN_SERVER` | e.g. `yourregistry.azurecr.io` |
| `ACR_USERNAME` | ACR admin username |
| `ACR_PASSWORD` | ACR admin password |

See `GITHUB_ACTIONS_SETUP.md` for the full step-by-step walkthrough including Azure resource provisioning.

---

## Docker

The `Dockerfile` is at the repository root (`inventory/Dockerfile`).

### Build and run locally

```bash
# From the inventory/ directory (where Dockerfile lives)
docker build -t inventory-aggregator .

docker run -p 8080:8080 \
  -e INVENTORYAPI__BASEURL="https://your-api-url/" \
  -e INVENTORYAPI__APIKEY="your-api-key" \
  inventory-aggregator
```

Access: `http://localhost:8080/swagger`

### Image stages

| Stage | Purpose |
|-------|---------|
| `base` | ASP.NET 11 runtime only — smallest final image, runs as non-root |
| `build` | SDK image — restores and builds |
| `test` | Runs all 36 unit tests inside Docker (can be skipped with `--target publish`) |
| `publish` | `dotnet publish` Release output |
| `final` | Copies publish output into `base` |

### Build skipping the test stage (faster CI when tests already ran)

```bash
docker build --target publish -t inventory-aggregator .
```

---

## Assumptions and Decisions

- **Zero-quantity records** are excluded from all aggregation figures but are visible in full via `/api/aggregation/loadall`.
- **Category grouping** is case-insensitive and whitespace-trimmed. Display name is taken from the first record in each group.
- **`totalInventoryValue`** is rounded to 2 decimal places using `MidpointRounding.AwayFromZero`.
- **Category ordering** is alphabetical (spec is silent on ordering).
- **Page size** is 25 (the API maximum) to minimise round trips.
- **Concurrent fetching** — page 1 is fetched first to discover `totalPages`, then all remaining pages are fetched in parallel with `Task.WhenAll` and merged back in page-number order.
- **429 Too Many Requests** — the retry policy handles 429 alongside 5xx. If the upstream sends a `Retry-After` header the wait honours it; otherwise exponential back-off applies (2 s / 4 s / 8 s).
