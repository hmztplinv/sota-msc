namespace Catalog.API.Models;

/// <summary>
/// Catalog domain modeli. Marten bunu JSON document olarak PostgreSQL'de saklayacak.
/// Neden class (record değil)? Marten deserialization için mutable property'ler istiyor.
/// Neden GUID Id? Marten convention — document identity.
/// </summary>
public class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;

    /// <summary>Ürün kategorileri. List — bir ürün birden fazla kategoride olabilir.</summary>
    public List<string> Categories { get; set; } = [];

    public string ImageFile { get; set; } = default!;
    public decimal Price { get; set; }
}
