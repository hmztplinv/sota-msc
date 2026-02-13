using Catalog.API.Models;
using Marten;
using Marten.Schema;

namespace Catalog.API.Data;

/// <summary>
/// Marten IInitialData — uygulama başlarken çalışır.
/// Eğer Products tablosu boşsa seed data ekler.
/// Production'da bu olmaz — sadece development/demo amaçlı.
/// </summary>
public sealed class CatalogInitialData : IInitialData
{
    public async Task Populate(IDocumentStore store, CancellationToken cancellation)
    {
        await using var session = store.LightweightSession();

        // Zaten veri varsa tekrar ekleme
        if (await session.Query<Product>().AnyAsync(cancellation))
            return;

        session.Store(GetPreconfiguredProducts().ToArray());
        await session.SaveChangesAsync(cancellation);
    }

    private static IReadOnlyList<Product> GetPreconfiguredProducts() =>
    [
        new Product
        {
            Id = Guid.NewGuid(),
            Name = "IPhone X",
            Description = "Apple smartphone with OLED display",
            Categories = ["Smartphone", "Electronics"],
            ImageFile = "product-1.png",
            Price = 950.00m
        },
        new Product
        {
            Id = Guid.NewGuid(),
            Name = "Samsung Galaxy S24",
            Description = "Samsung flagship with AI features",
            Categories = ["Smartphone", "Electronics"],
            ImageFile = "product-2.png",
            Price = 840.00m
        },
        new Product
        {
            Id = Guid.NewGuid(),
            Name = "Huawei Mate 60",
            Description = "Huawei premium smartphone",
            Categories = ["Smartphone", "Electronics"],
            ImageFile = "product-3.png",
            Price = 650.00m
        },
        new Product
        {
            Id = Guid.NewGuid(),
            Name = "MacBook Pro 16",
            Description = "Apple M3 Pro laptop for professionals",
            Categories = ["Laptop", "Electronics"],
            ImageFile = "product-4.png",
            Price = 2499.00m
        },
        new Product
        {
            Id = Guid.NewGuid(),
            Name = "Dell XPS 15",
            Description = "Dell premium ultrabook",
            Categories = ["Laptop", "Electronics"],
            ImageFile = "product-5.png",
            Price = 1799.00m
        }
    ];
}
