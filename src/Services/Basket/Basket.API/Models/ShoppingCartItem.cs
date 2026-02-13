namespace Basket.API.Models;

/// <summary>
/// Sepetteki tek bir ürün satırı.
/// Catalog servisindeki Product'ın sepet temsili.
/// </summary>
public sealed class ShoppingCartItem
{
    public int Quantity { get; set; }
    public string Color { get; set; } = default!;
    public decimal Price { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = default!;
}