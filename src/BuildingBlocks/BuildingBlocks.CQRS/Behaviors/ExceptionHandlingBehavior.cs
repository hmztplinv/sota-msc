using BuildingBlocks.Results;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.CQRS.Behaviors;

public sealed class ExceptionHandlingBehavior<TRequest, TResponse>(
    ILogger<ExceptionHandlingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (Exception ex)
        {
            var requestName = typeof(TRequest).Name;

            logger.LogError(ex,
                "[UNHANDLED EXCEPTION] Request {RequestName} — {ExceptionMessage}",
                requestName, ex.Message);

            // Result<T> dönen handler'lar için failure'a çevirmeyi dene
            if (TryCreateFailureResult(ex, out var result))
            {
                return result;
            }

            // Result<T> değilse → exception'ı yukarı fırlat
            // IExceptionHandler middleware yakalayacak (Adım 2)
            throw;
        }
    }

    /// <summary>
    /// TResponse bir IResultBase implementasyonu ise (Result veya Result{T}),
    /// exception'ı Result.Failure'a çevirir.
    /// Reflection kullanıyoruz çünkü generic TResponse'un runtime tipini bilmiyoruz.
    /// </summary>
    private static bool TryCreateFailureResult(Exception ex, out TResponse result)
    {
        result = default!;

        var responseType = typeof(TResponse);

        // TResponse, IResultBase'i implement etmiyor → false
        if (!typeof(IResultBase).IsAssignableFrom(responseType))
            return false;

        var error = Error.Unexpected(
            code: "Unhandled",
            description: ex.Message);

        // Case 1: Result (non-generic)
        if (responseType == typeof(Result))
        {
            result = (TResponse)(object)Result.Failure(error);
            return true;
        }

        // Case 2: Result<T> (generic)
        if (responseType.IsGenericType &&
            responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var innerType = responseType.GetGenericArguments()[0];
            var failureMethod = typeof(Result<>)
                .MakeGenericType(innerType)
                .GetMethod(nameof(Result.Failure), [typeof(Error)])!;

            result = (TResponse)failureMethod.Invoke(null, [error])!;
            return true;
        }

        return false;
    }
}