// src/BuildingBlocks/BuildingBlocks.CQRS/Behaviors/LoggingBehavior.cs

using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.CQRS.Behaviors;

/// <summary>
/// Her request'in başlangıç, bitiş ve süresini loglar.
/// Generic — tüm Command ve Query'ler için otomatik çalışır.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        logger.LogInformation("[START] {RequestName}", requestName);

        var stopwatch = Stopwatch.StartNew();

        var response = await next();

        stopwatch.Stop();

        if (stopwatch.ElapsedMilliseconds > 500)
        {
            logger.LogWarning("[SLOW] {RequestName} took {ElapsedMs}ms",
                requestName, stopwatch.ElapsedMilliseconds);
        }

        logger.LogInformation("[END] {RequestName} completed in {ElapsedMs}ms",
            requestName, stopwatch.ElapsedMilliseconds);

        return response;
    }
}