using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Catalog.API.Data;
using Carter;
using Marten;
using Serilog;
using FluentValidation;
using Scalar.AspNetCore;
// --- Serilog Bootstrap Logger ---
// Uygulama ayağa kalkarken oluşan hataları yakalamak için early logger
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Catalog.API starting up...");

    var builder = WebApplication.CreateBuilder(args);

    // --- Serilog ---
    // Bootstrap logger'ı configuration-aware logger ile değiştir
    builder.Host.UseSerilog((context, loggerConfig) =>
        loggerConfig
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.WithProperty("Application", "Catalog.API"));

    // --- Marten (Document DB on PostgreSQL) ---
    // Marten, PostgreSQL'i document database olarak kullanmamızı sağlar.
    // JSONB column'larda Product objeleri serialize edilir — şema esnekliği.
    builder.Services.AddMarten(options =>
    {
        var connectionString = builder.Configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Database connection string is required.");

        options.Connection(connectionString);

        // Development'ta schema'yı otomatik oluştur/güncelle
        if (builder.Environment.IsDevelopment())
            options.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;

        }).InitializeWith<CatalogInitialData>().UseLightweightSessions(); // Lightweight session — performans için yeterli, Unit of Work gerektirmiyoruz

    // --- MediatR ---
    // CQRS handler'ları otomatik register et (bu assembly'deki tüm IRequestHandler'lar)
    // --- MediatR + Pipeline Behaviors ---
    // CQRS handler'ları otomatik register et (bu assembly'deki tüm IRequestHandler'lar)
    builder.Services.AddMediatR(config =>
    {
        config.RegisterServicesFromAssembly(typeof(Program).Assembly);

        // Pipeline Behaviors — her request'te sırayla çalışır:
        // 1) Logging → request başlangıç/bitiş + süre ölçümü
        // 2) Validation → FluentValidation rules, handler'a ulaşmadan önce kontrol
        // 3) ExceptionHandling → beklenmeyen exception → Result.Failure(Unexpected)
        config.AddOpenBehavior(typeof(BuildingBlocks.CQRS.Behaviors.LoggingBehavior<,>));
        config.AddOpenBehavior(typeof(BuildingBlocks.CQRS.Behaviors.ValidationBehavior<,>));
        config.AddOpenBehavior(typeof(BuildingBlocks.CQRS.Behaviors.ExceptionHandlingBehavior<,>));
    });

    builder.Services.AddOpenApi();


    // --- FluentValidation ---
    // Bu assembly'deki tüm AbstractValidator<T>'leri otomatik register et
    builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

    // --- Global Exception Handling ---
    // IExceptionHandler — MediatR pipeline dışında kalan exception'ları da yakalar
    builder.Services.AddExceptionHandler<Catalog.API.Exceptions.CustomExceptionHandler>();
    builder.Services.AddProblemDetails(); // Problem Details serialization desteği

    // --- Carter ---
    // Minimal API endpoint modüllerini otomatik keşfet ve register et
    builder.Services.AddCarter();

    // --- Health Checks ---
    builder.Services.AddHealthChecks()
        .AddNpgSql(builder.Configuration.GetConnectionString("Database") ?? throw new InvalidOperationException("Database connection string is required for health check."));

    var app = builder.Build();

    // --- Middleware Pipeline ---
    // Serilog request logging — her HTTP request otomatik loglanır
    app.UseSerilogRequestLogging();

    app.UseExceptionHandler(); // Global exception handling middleware

    // --- OpenAPI + Scalar UI ---
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();                    // /openapi/v1.json endpoint'i
        app.MapScalarApiReference(options => // Scalar UI
        {
            options.Title = "Catalog API";
            options.Theme = ScalarTheme.Mars;
        });
    }

    // Carter endpoint'lerini map et
    app.MapCarter();

    // Health check — basit başlangıç, sonra genişletilecek
    app.MapHealthChecks("/health");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Catalog.API terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
