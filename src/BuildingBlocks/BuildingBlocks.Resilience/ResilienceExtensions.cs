namespace BuildingBlocks.Resilience;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

/// <summary>
/// Generic resilience pipeline builder.
/// Tüm servisler bu extension method'ları kullanarak
/// Retry + Circuit Breaker + Timeout pipeline'ı oluşturabilir.
/// 
/// Pipeline sırası (dıştan içe):
///   Total Timeout → Retry → Circuit Breaker → Per-Attempt Timeout → Actual Call
/// 
/// Neden bu sıra?
/// - Total Timeout: Tüm retry'lar dahil maksimum süre
/// - Retry: Geçici hatalarda tekrar dene
/// - Circuit Breaker: Sürekli fail'de devreyi aç, cascade failure önle
/// - Per-Attempt Timeout: Tek bir denemenin max süresi
/// </summary>
public static class ResilienceExtensions
{
    /// <summary>
    /// DI container'a named resilience pipeline ekler.
    /// Kullanım: services.AddDefaultResiliencePipeline("redis-pipeline");
    /// </summary>
    public static IServiceCollection AddDefaultResiliencePipeline(
        this IServiceCollection services,
        string pipelineName,
        Action<ResiliencePipelineOptions>? configure = null)
    {
        var options = new ResiliencePipelineOptions();
        configure?.Invoke(options);

        services.AddResiliencePipeline(pipelineName, (builder, context) =>
        {
            // 1️⃣ Total Timeout — tüm retry'lar dahil max süre
            builder.AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = options.TotalTimeout,
                Name = $"{pipelineName}-total-timeout",
                OnTimeout = args =>
                {
                    var logger = context.ServiceProvider
                        .GetService<ILoggerFactory>()?
                        .CreateLogger("Resilience");

                    logger?.LogWarning(
                        "[RESILIENCE] Total timeout exceeded for pipeline '{Pipeline}' after {Timeout}s",
                        pipelineName, options.TotalTimeout.TotalSeconds);

                    return default;
                }
            });

            // 2️⃣ Retry — exponential backoff ile tekrar dene
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                Delay = options.RetryBaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true, // Thundering herd önleme
                Name = $"{pipelineName}-retry",
                ShouldHandle = new PredicateBuilder()
                    .Handle<TimeoutRejectedException>()
                    .Handle<BrokenCircuitException>()
                    .Handle<Exception>(ex => ex is not OperationCanceledException),
                OnRetry = args =>
                {
                    var logger = context.ServiceProvider
                        .GetService<ILoggerFactory>()?
                        .CreateLogger("Resilience");

                    logger?.LogWarning(
                        "[RESILIENCE] Retry {AttemptNumber}/{MaxRetry} for pipeline '{Pipeline}' — delay {Delay}ms — {Exception}",
                        args.AttemptNumber + 1,
                        options.MaxRetryAttempts,
                        pipelineName,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.Message ?? "No exception");

                    return default;
                }
            });

            // 3️⃣ Circuit Breaker — sürekli fail'de devreyi aç
            builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = options.CircuitBreakerFailureRatio,
                SamplingDuration = options.CircuitBreakerSamplingDuration,
                MinimumThroughput = options.CircuitBreakerMinimumThroughput,
                BreakDuration = options.CircuitBreakerBreakDuration,
                Name = $"{pipelineName}-circuit-breaker",
                ShouldHandle = new PredicateBuilder()
                    .Handle<Exception>(ex => ex is not OperationCanceledException),
                OnOpened = args =>
                {
                    var logger = context.ServiceProvider
                        .GetService<ILoggerFactory>()?
                        .CreateLogger("Resilience");

                    logger?.LogError(
                        "[RESILIENCE] Circuit OPENED for pipeline '{Pipeline}' — break duration {Duration}s",
                        pipelineName, args.BreakDuration.TotalSeconds);

                    return default;
                },
                OnClosed = args =>
                {
                    var logger = context.ServiceProvider
                        .GetService<ILoggerFactory>()?
                        .CreateLogger("Resilience");

                    logger?.LogInformation(
                        "[RESILIENCE] Circuit CLOSED for pipeline '{Pipeline}' — recovered",
                        pipelineName);

                    return default;
                }
            });

            // 4️⃣ Per-Attempt Timeout — tek deneme max süresi
            builder.AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = options.PerAttemptTimeout,
                Name = $"{pipelineName}-per-attempt-timeout"
            });
        });

        return services;
    }
}

/// <summary>
/// Resilience pipeline konfigürasyon seçenekleri.
/// Default değerler production-ready; override edilebilir.
/// </summary>
public sealed class ResiliencePipelineOptions
{
    // --- Retry ---
    /// <summary>Maksimum tekrar deneme sayısı</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>İlk retry'dan önceki bekleme süresi (exponential backoff base)</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    // --- Circuit Breaker ---
    /// <summary>Devreyi açmak için gereken hata oranı (0.0 - 1.0)</summary>
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;

    /// <summary>Hata oranı ölçüm penceresi</summary>
    public TimeSpan CircuitBreakerSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Devreyi açmadan önce minimum istek sayısı</summary>
    public int CircuitBreakerMinimumThroughput { get; set; } = 5;

    /// <summary>Devre açıldıktan sonra bekleme süresi</summary>
    public TimeSpan CircuitBreakerBreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    // --- Timeout ---
    /// <summary>Tek bir denemenin max süresi</summary>
    public TimeSpan PerAttemptTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Tüm retry'lar dahil toplam max süre</summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(30);
}