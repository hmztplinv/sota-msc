// src/BuildingBlocks/BuildingBlocks.CQRS/Behaviors/ValidationBehavior.cs

using BuildingBlocks.Results;
using FluentValidation;
using MediatR;

namespace BuildingBlocks.CQRS.Behaviors;

/// <summary>
/// FluentValidation ile otomatik validation.
/// Eğer request için bir AbstractValidator tanımlıysa, handler'dan ÖNCE çalışır.
/// Validator yoksa → direkt next()'e geçer (opt-in pattern).
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : IResultBase
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var errors = validationResults
            .SelectMany(r => r.Errors)
            .Where(e => e is not null)
            .ToList();

        if (errors.Count == 0)
            return await next();

        // Validation hataları → Result.Failure olarak dön
        var errorMessage = string.Join("; ", errors.Select(e => e.ErrorMessage));
        var error = Error.Validation("Validation", errorMessage);

        // TResponse'ı Result<T> veya Result olarak oluştur
        return (TResponse)CreateValidationResult(typeof(TResponse), error);
    }

    private static IResultBase CreateValidationResult(Type resultType, Error error)
    {
        // Result<T> ise → generic tipi bul ve Result<T>.Failure() çağır
        if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var innerType = resultType.GetGenericArguments()[0];
            var failureMethod = typeof(Result<>)
                .MakeGenericType(innerType)
                .GetMethod(nameof(Result<object>.Failure), [typeof(Error)])!;

            return (IResultBase)failureMethod.Invoke(null, [error])!;
        }

        // Result ise → direkt Result.Failure()
        return Result.Failure(error);
    }
}