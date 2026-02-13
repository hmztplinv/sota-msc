namespace Basket.API.Models;

/// <summary>
/// Kullanıcının alışveriş sepeti.
/// Redis'te UserName key olarak saklanır.
/// </summary>
public sealed class ShoppingCart
{
    public string UserName { get; set; } = default!;
    public List<ShoppingCartItem> Items { get; set; } = [];

    // Hesaplanmış toplam fiyat — Redis'te saklanmaz, her seferinde hesaplanır
    public decimal TotalPrice => Items.Sum(item => item.Price * item.Quantity);

    // Parameterless constructor — JSON deserialization için gerekli
    public ShoppingCart() { }

    // Primary constructor — business logic'te kullanılır
    public ShoppingCart(string userName)
    {
        UserName = userName;
    }
}