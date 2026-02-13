using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Catalog.API.Data;
using Carter;
using Marten;
using Serilog;

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
    builder.Services.AddMediatR(config =>
        config.RegisterServicesFromAssembly(typeof(Program).Assembly));

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
