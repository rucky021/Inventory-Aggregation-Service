# =============================================================================
# Multi-stage Dockerfile — Inventory Aggregation Service
#
# Stages:
#   base    – runtime image (smaller, no SDK)
#   build   – restore + build
#   publish – dotnet publish (Release, trimmed)
#   final   – copy publish output into base image
#
# Build:  docker build -t inventory-aggregator .
# Run:    docker run -p 8080:8080 \
#           -e INVENTORYAPI__BASEURL="https://your-api-url/" \
#           -e INVENTORYAPI__APIKEY="your-key" \
#           inventory-aggregator
#
# Swagger UI: http://localhost:8080/swagger
# Endpoint:   http://localhost:8080/api/aggregation
#             http://localhost:8080/api/aggregation/loadall
# =============================================================================

# ── Stage 1: base runtime image ──────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS base
WORKDIR /app

# Listen on HTTP 8080 inside the container.
# HTTPS termination is handled by the reverse proxy / App Service in front of the container.
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Run as a non-root user for security
RUN adduser --disabled-password --gecos "" appuser
USER appuser

# ── Stage 2: build ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src

# Copy solution and project files first so Docker layer caching skips restore
# on subsequent builds when only source files change.
COPY project/InventoryAggregator.slnx ./
COPY project/src/InventoryAggregator/InventoryAggregator.csproj project/src/InventoryAggregator/
COPY project/tests/InventoryAggregator.Tests/InventoryAggregator.Tests.csproj project/tests/InventoryAggregator.Tests/

RUN dotnet restore project/src/InventoryAggregator/InventoryAggregator.csproj

# Copy remaining source
COPY project/ project/

# ── Stage 3: test (optional — comment out the RUN line to skip in CI) ────────
FROM build AS test
RUN dotnet test project/tests/InventoryAggregator.Tests/InventoryAggregator.Tests.csproj \
    --configuration Release \
    --no-restore \
    --logger "console;verbosity=normal"

# ── Stage 4: publish ──────────────────────────────────────────────────────────
FROM build AS publish
RUN dotnet publish project/src/InventoryAggregator/InventoryAggregator.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# ── Stage 5: final image ──────────────────────────────────────────────────────
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "InventoryAggregator.dll"]
