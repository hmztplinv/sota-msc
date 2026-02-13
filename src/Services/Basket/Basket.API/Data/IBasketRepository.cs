namespace Basket.API.Data;

using Basket.API.Models;

/// <summary>
/// Basket veri erişim soyutlaması.
/// Underlying storage Redis, ama consumer'lar bunu bilmek zorunda değil.
/// </summary>
public interface IBasketRepository
{
    Task<ShoppingCart?> GetBasketAsync(string userName, CancellationToken cancellationToken = default);
    Task<ShoppingCart> StoreBasketAsync(ShoppingCart basket, CancellationToken cancellationToken = default);
    Task<bool> DeleteBasketAsync(string userName, CancellationToken cancellationToken = default);
}