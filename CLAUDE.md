# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Build the entire solution
dotnet build sota-eshop-microservices.sln

# Build a specific service
dotnet build src/Services/Catalog/Catalog.API/Catalog.API.csproj

# Run a specific service (from repo root)
dotnet run --project src/Services/Catalog/Catalog.API/Catalog.API.csproj
dotnet run --project src/Services/Basket/Basket.API/Basket.API.csproj
dotnet run --project src/Services/Discount/Discount.Grpc/Discount.Grpc.csproj

# Start infrastructure (PostgreSQL + Redis) via Docker
cd docker && docker compose up -d postgres redis

# Start all services via Docker
cd docker && docker compose up -d

# EF Core migrations (Discount.Grpc only — uses EF Core + PostgreSQL)
dotnet ef migrations add <MigrationName> --project src/Services/Discount/Discount.Grpc
dotnet ef database update --project src/Services/Discount/Discount.Grpc
```

## Architecture Overview

**Target framework:** .NET 9. All projects use `nullable enable` and `implicit usings`.

### Service Layout

```
src/
  BuildingBlocks/
    BuildingBlocks.CQRS       — ICommand, IQuery, ICommandHandler, IQueryHandler + MediatR pipeline behaviors
    BuildingBlocks.Results     — Result<T> / Result (Railway-Oriented Programming pattern)
    BuildingBlocks.Resilience  — AddDefaultResiliencePipeline() extension (Polly: retry + circuit breaker + timeout)
  Services/
    Catalog/Catalog.API        — REST API, Marten (document DB on PostgreSQL)
    Basket/Basket.API          — REST API, Redis + HybridCache
    Discount/Discount.Grpc     — gRPC service, EF Core + PostgreSQL
docker/
  docker-compose.yml           — PostgreSQL :5432, Redis :6379
  init-databases.sql           — Creates CatalogDb, DiscountDb databases
monitoring/
  prometheus/, grafana/, jaeger/
```

### Vertical Slice Architecture (Catalog & Basket)

Each feature lives in its own folder under `Features/`. A slice contains:
- **Handler file** — defines the `record` Command/Query + `record` Result + `internal sealed class` Handler implementing `ICommandHandler<,>` or `IQueryHandler<,>` from BuildingBlocks.CQRS
- **Endpoint file** — Carter `ICarterModule` that maps the HTTP route, uses Mapster to adapt request → command, calls `ISender.Send()`, and maps `Result<T>` to HTTP response
- **Validator file** (when needed) — FluentValidation `AbstractValidator<T>`, auto-registered via `AddValidatorsFromAssembly`

### MediatR Pipeline Order (Catalog & Basket)

Every request flows through (in order):
1. `LoggingBehavior` — logs request start/end + elapsed time
2. `ValidationBehavior` — runs FluentValidation; returns `Result.Failure` on error (no exception thrown to caller)
3. `ExceptionHandlingBehavior` — catches unhandled exceptions → `Result.Failure(Error.Unexpected)`

### Result Pattern

Handlers return `Result<T>` (or `Result` for void operations). Endpoints check `result.IsSuccess` and map accordingly. Errors are typed via `BuildingBlocks.Results.Error`.

### Basket Caching

`IBasketRepository` is decorated via Scrutor:
- `BasketRepository` — raw Redis (StackExchange.Redis)
- `CachedBasketRepository` — wraps inner with .NET 9 `HybridCache` (L1 in-memory 5 min + L2 Redis 30 min, stampede-protected)

DI resolves as: Handler → `CachedBasketRepository` → `BasketRepository`.

### Discount gRPC

- Proto file: `src/Services/Discount/Discount.Grpc/Protos/discount.proto`
- Service: `DiscountService` (CRUD on `Coupon` entity)
- Database auto-migrated on startup via `UseMigrationAsync()` in `Data/Extensions.cs`
- gRPC reflection enabled in Development for tooling (e.g. Postman, grpcurl)

### Infrastructure (Docker)

| Service | Port | Used by |
|---|---|---|
| PostgreSQL | 5432 | Catalog.API (Marten), Discount.Grpc (EF Core) |
| Redis | 6379 | Basket.API |
| catalog-api | 5001 | — |
| basket-api | 5002 | — |
| discount-grpc | 5003 | — |

Connection strings in `appsettings.json` use `ConnectionStrings__Database` / `ConnectionStrings__Redis` naming convention.

### API Documentation

Scalar UI is mounted in Development at `/scalar/v1` (Catalog) and `/scalar/v1` (Basket). OpenAPI spec at `/openapi/v1.json`.

### Conventions

- All command/query records and result records are co-located in the handler file.
- Endpoints use Mapster with zero-config (property-name matching) for request → command mapping.
- Comments in source files are in Turkish (this is a Turkish-language course project).
- `internal sealed class` for handlers; `public sealed record` for commands/queries/results.
- Serilog bootstrapped before `WebApplication.CreateBuilder` for early startup error capture; wraps entire `try/catch/finally` around `app.Run()`.
