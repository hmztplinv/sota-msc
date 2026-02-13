namespace Basket.API.Data;

using Basket.API.Models;
using Microsoft.Extensions.Caching.Distributed;
using Polly;
using Polly.Registry;
using System.Text.Json;

/// <summary>
/// Raw Redis storage repository — resilience pipeline ile korumalı.
/// Her Redis çağrısı Retry + Circuit Breaker + Timeout pipeline'ından geçer.
/// </summary>
public sealed class BasketRepository(
    IDistributedCache cache,
    ResiliencePipelineProvider<string> pipelineProvider) : IBasketRepository
{
    private static string CacheKey(string userName) => $"basket:{userName}";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Lazy pipeline resolution — ilk kullanımda resolve edilir
    private ResiliencePipeline Pipeline => pipelineProvider.GetPipeline("redis-pipeline");

    public async Task<ShoppingCart?> GetBasketAsync(string userName, CancellationToken cancellationToken = default)
    {
        // Resilience pipeline ile sarmalanmış Redis çağrısı
        return await Pipeline.ExecuteAsync(async ct =>
        {
            var json = await cache.GetStringAsync(CacheKey(userName), ct);

            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonSerializer.Deserialize<ShoppingCart>(json, _jsonOptions);
        }, cancellationToken);
    }

    public async Task<ShoppingCart> StoreBasketAsync(ShoppingCart basket, CancellationToken cancellationToken = default)
    {
        return await Pipeline.ExecuteAsync(async ct =>
        {
            var json = JsonSerializer.Serialize(basket, _jsonOptions);

            await cache.SetStringAsync(
                CacheKey(basket.UserName),
                json,
                new DistributedCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(30)
                },
                ct);

            return basket;
        }, cancellationToken);
    }

    public async Task<bool> DeleteBasketAsync(string userName, CancellationToken cancellationToken = default)
    {
        return await Pipeline.ExecuteAsync(async ct =>
        {
            await cache.RemoveAsync(CacheKey(userName), ct);
            return true;
        }, cancellationToken);
    }
}