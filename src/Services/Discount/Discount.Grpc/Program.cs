using Discount.Grpc.Data;
using Discount.Grpc.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// DbContext — PostgreSQL
builder.Services.AddDbContext<DiscountDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));

// gRPC
builder.Services.AddGrpc();
builder.Services.AddGrpcReflection();

var app = builder.Build();

// Auto Migration + Seed
await app.UseMigrationAsync();

// gRPC Service endpoint
app.MapGrpcService<DiscountService>();

// gRPC Reflection — development'ta test kolaylığı
if (app.Environment.IsDevelopment())
{
    app.MapGrpcReflectionService();
}

app.MapGet("/", () => "Discount gRPC Service is running.");

app.Run();
