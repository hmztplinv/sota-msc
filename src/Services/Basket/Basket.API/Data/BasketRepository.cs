namespace Basket.API.Data;

using Basket.API.Models;
using Microsoft.Extensions.Caching.Hybrid;

/// <summary>
/// HybridCache backed basket repository.
/// L1 (in-memory) + L2 (Redis) — .NET 9 SOTA caching.
/// </summary>
public sealed class BasketRepository(HybridCache cache) : IBasketRepository
{
    // Cache key convention: "basket:{userName}"
    private static string CacheKey(string userName) => $"basket:{userName}";

    public async Task<ShoppingCart?> GetBasketAsync(string userName, CancellationToken cancellationToken = default)
    {
        // HybridCache.GetOrCreateAsync: L1 → L2 → factory
        // Factory null dönerse cache'e yazılmaz
        var basket = await cache.GetOrCreateAsync(
            CacheKey(userName),
            cancellationToken: cancellationToken,
            factory: _ => ValueTask.FromResult<ShoppingCart?>(null));

        return basket;
    }

    public async Task<ShoppingCart> StoreBasketAsync(ShoppingCart basket, CancellationToken cancellationToken = default)
    {
        // SetAsync: Hem L1 hem L2'ye yazar
        await cache.SetAsync(
            CacheKey(basket.UserName),
            basket,
            cancellationToken: cancellationToken);

        return basket;
    }

    public async Task<bool> DeleteBasketAsync(string userName, CancellationToken cancellationToken = default)
    {
        // RemoveAsync: Hem L1 hem L2'den siler
        await cache.RemoveAsync(CacheKey(userName), cancellationToken);
        return true;
    }
}