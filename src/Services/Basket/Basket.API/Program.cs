using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Basket.API.Data;
using Carter;
using Serilog;
using FluentValidation;
using Scalar.AspNetCore;
using BuildingBlocks.Resilience;

// --- Serilog Bootstrap Logger ---
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Basket.API starting up...");

    var builder = WebApplication.CreateBuilder(args);

    // --- Serilog ---
    builder.Host.UseSerilog((context, loggerConfig) =>
        loggerConfig
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.WithProperty("Application", "Basket.API"));

    // --- Redis + HybridCache ---
    // L2 backend: Redis distributed cache
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = builder.Configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Redis connection string is required.");
    });

    // HybridCache: L1 (in-memory) + L2 (Redis) — .NET 9 SOTA
    // Stampede protection dahil — aynı key için eşzamanlı istekler tek factory çağrısı yapar
    builder.Services.AddHybridCache(options =>
    {
        options.DefaultEntryOptions = new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions
        {
            // L1 in-memory cache süresi (kısa — memory pressure)
            LocalCacheExpiration = TimeSpan.FromMinutes(5),
            // L2 Redis cache süresi (uzun — distributed)
            Expiration = TimeSpan.FromMinutes(30)
        };
    });

    // --- Resilience Pipeline ---
    // Redis çağrıları için: Retry (3x exponential) + Circuit Breaker + Timeout
    builder.Services.AddDefaultResiliencePipeline("redis-pipeline", options =>
    {
        options.MaxRetryAttempts = 3;
        options.RetryBaseDelay = TimeSpan.FromMilliseconds(300);
        options.PerAttemptTimeout = TimeSpan.FromSeconds(3);
        options.TotalTimeout = TimeSpan.FromSeconds(15);
        options.CircuitBreakerFailureRatio = 0.5;
        options.CircuitBreakerBreakDuration = TimeSpan.FromSeconds(15);
    });

    // --- Repository + Decorator (Scrutor) ---
    // 1. İlk olarak BasketRepository register edilir
    // 2. Scrutor Decorate ile CachedBasketRepository sarmalanır
    // DI resolve sırası: Handler → CachedBasketRepository → BasketRepository
    builder.Services.AddScoped<IBasketRepository, BasketRepository>();
    builder.Services.Decorate<IBasketRepository, CachedBasketRepository>();

    // --- MediatR + Pipeline Behaviors ---
    builder.Services.AddMediatR(config =>
    {
        config.RegisterServicesFromAssembly(typeof(Program).Assembly);

        config.AddOpenBehavior(typeof(BuildingBlocks.CQRS.Behaviors.LoggingBehavior<,>));
        config.AddOpenBehavior(typeof(BuildingBlocks.CQRS.Behaviors.ValidationBehavior<,>));
        config.AddOpenBehavior(typeof(BuildingBlocks.CQRS.Behaviors.ExceptionHandlingBehavior<,>));
    });

    // --- FluentValidation ---
    builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

    // --- Global Exception Handling ---
    // TODO: Bölüm 5'te Basket-specific CustomExceptionHandler eklenecek
    builder.Services.AddProblemDetails();

    // --- Carter ---
    builder.Services.AddCarter();

    // --- OpenAPI ---
    builder.Services.AddOpenApi();

    // --- Health Checks ---
    builder.Services.AddHealthChecks()
        .AddRedis(
            builder.Configuration.GetConnectionString("Redis")
                ?? throw new InvalidOperationException("Redis connection string is required for health check."),
            name: "redis",
            tags: ["ready"]);

    var app = builder.Build();

    // --- Middleware Pipeline ---
    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference(options =>
        {
            options.Title = "Basket API";
            options.Theme = ScalarTheme.BluePlanet;
        });
    }

    app.MapCarter();
    app.MapHealthChecks("/health");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Basket.API terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}