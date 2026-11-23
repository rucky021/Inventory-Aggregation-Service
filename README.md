# Inventory Aggregation Service

A .NET 11 Web API that reads inventory records from the upstream Inventory API, aggregates them by category, and exposes the result through a single HTTP endpoint.

---

## Prerequisites

- [.NET 11 SDK](https://dotnet.microsoft.com/download) — verify with `dotnet --version`
- The Inventory API base URL and key (provided with the assignment)

---

## Step 1 — Configure the API Base URL and Key

The app requires two values before it will start. Choose one of the three options below.

---

### Option 1: User Secrets *(recommended for development)*

User secrets are stored outside the project directory and are never committed to source control.

Open a terminal in the `InventoryAggregator` project directory:

```powershell
cd <path-to-your-project>\InventoryAggregator
```

Initialise user secrets (only needed once — adds a `UserSecretsId` to the `.csproj`):

```powershell
dotnet user-secrets init
```

Set the base URL:

```powershell
dotnet user-secrets set "InventoryApi:BaseUrl" "<your-api-base-url>"
```

Set the API key:

```powershell
dotnet user-secrets set "InventoryApi:ApiKey" "<your-api-key>"
```

Verify the values were saved:

```powershell
dotnet user-secrets list
```

---

### Option 2: Environment Variables

The settings apply only to the current terminal session.

**PowerShell:**

```powershell
$env:INVENTORYAPI__BASEURL = "<your-api-base-url>"
$env:INVENTORYAPI__APIKEY  = "<your-api-key>"
```

**Command Prompt:**

```cmd
set INVENTORYAPI__BASEURL=<your-api-base-url>
set INVENTORYAPI__APIKEY=<your-api-key>
```

**bash / macOS / Linux:**

```bash
export INVENTORYAPI__BASEURL="<your-api-base-url>"
export INVENTORYAPI__APIKEY="<your-api-key>"
```

> The double underscore `__` maps to the `:` separator in nested .NET config keys.


---

## Step 2 — Run the App

```powershell
cd <path-to-your-project>\InventoryAggregator
dotnet run
```

The console will print the listening address, for example:

```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:5202
```

**First run only** — trust the HTTPS development certificate:

```powershell
dotnet dev-certs https --trust
```

---

## Step 3 — Call the Endpoint

### Via Swagger UI (easiest)

Open a browser and navigate to:

```
https://localhost:5202/swagger
```

1. Expand **GET /api/aggregation**
2. Click **Try it out**
3. Click **Execute**
4. The response body appears below with the full aggregated dataset

---

### Via curl

```bash
curl -k https://localhost:5202/api/aggregation
```

### Via PowerShell

```powershell
Invoke-RestMethod -Uri "https://localhost:5202/api/aggregation" -SkipCertificateCheck
```

---

### Example response

```json
{
  "categories": [
    {
      "category": "Electronics",
      "totalInventoryValue": 1799.88,
      "availableRecordCount": 1,
      "mostRecentlyUpdated": {
        "id": 1001,
        "name": "Thermal Receipt Printer",
        "lastUpdated": "2026-10-15T12:30:00Z"
      },
      "items": [
        {
          "id": 1001,
          "name": "Thermal Receipt Printer",
          "category": "Electronics",
          "price": 149.99,
          "quantity": 12,
          "lastUpdated": "2026-10-15T12:30:00Z"
        }
      ]
    }
  ]
}
```

If the upstream API is unreachable or returns an error the endpoint returns `502` with a structured JSON body — it never surfaces an unhandled exception.

---

## Step 4 — Run the Tests

```powershell
cd  <path-to-your-project>\inventory\project\tests\InventoryAggregator.Tests
dotnet test
```

Expected output: **26 tests, 0 failures.**

For verbose output:

```powershell
dotnet test --logger "console;verbosity=normal"
```

---