using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.API.Exceptions;

public sealed class CustomExceptionHandler(ILogger<CustomExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception,
            "[GLOBAL EXCEPTION] {ExceptionType} — {Message}",
            exception.GetType().Name, exception.Message);

        // Exception tipine göre status code belirle
        var (statusCode, title) = exception switch
        {
            ArgumentException => (StatusCodes.Status400BadRequest, "Bad Request"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            FluentValidation.ValidationException fluentEx =>
                (StatusCodes.Status400BadRequest, "Validation Error"),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error")
        };

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = exception.Message,
            Instance = httpContext.Request.Path,
            Type = $"https://httpstatuses.com/{statusCode}"
        };

        // FluentValidation hatalarını extensions'a ekle
        if (exception is FluentValidation.ValidationException validationEx)
        {
            problemDetails.Extensions["errors"] = validationEx.Errors
                .Select(e => new { e.PropertyName, e.ErrorMessage })
                .ToArray();
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        // true → exception handled, pipeline durur
        return true;
    }
}