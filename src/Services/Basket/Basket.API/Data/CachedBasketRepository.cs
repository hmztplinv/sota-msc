namespace Basket.API.Data;

using Basket.API.Models;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

/// <summary>
/// Decorator: HybridCache (L1 in-memory + L2 Redis) katmanı ekler.
/// Inner repository'ye delege eder; cache miss'te inner'dan okur.
/// 
/// Pattern: Decorator (Scrutor ile DI'da register edilir)
/// Avantaj: BasketRepository cache'den habersiz, test edilebilir.
/// </summary>
public sealed class CachedBasketRepository(
    IBasketRepository innerRepository,
    HybridCache cache,
    ILogger<CachedBasketRepository> logger) : IBasketRepository
{
    private static string CacheKey(string userName) => $"basket:{userName}";

    public async Task<ShoppingCart?> GetBasketAsync(string userName, CancellationToken cancellationToken = default)
    {
        // HybridCache: L1 → L2 → factory (inner repository)
        // Stampede protection dahil
        var basket = await cache.GetOrCreateAsync(
            CacheKey(userName),
            async ct =>
            {
                logger.LogInformation("Cache miss for basket '{UserName}' — fetching from store", userName);
                return await innerRepository.GetBasketAsync(userName, ct);
            },
            cancellationToken: cancellationToken);

        return basket;
    }

    public async Task<ShoppingCart> StoreBasketAsync(ShoppingCart basket, CancellationToken cancellationToken = default)
    {
        // 1. Inner repository'ye yaz (Redis raw store)
        var stored = await innerRepository.StoreBasketAsync(basket, cancellationToken);

        // 2. HybridCache'i güncelle (L1 + L2 sync)
        await cache.SetAsync(
            CacheKey(basket.UserName),
            stored,
            cancellationToken: cancellationToken);

        logger.LogInformation("Basket '{UserName}' stored and cache updated", basket.UserName);

        return stored;
    }

    public async Task<bool> DeleteBasketAsync(string userName, CancellationToken cancellationToken = default)
    {
        // 1. Inner repository'den sil
        var deleted = await innerRepository.DeleteBasketAsync(userName, cancellationToken);

        // 2. Cache'den de kaldır
        await cache.RemoveAsync(CacheKey(userName), cancellationToken);

        logger.LogInformation("Basket '{UserName}' deleted and cache invalidated", userName);

        return deleted;
    }
}