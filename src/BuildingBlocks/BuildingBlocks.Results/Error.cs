namespace BuildingBlocks.Results;

/// <summary>
/// Hata bilgisini taşıyan immutable value object.
/// Record kullanıyoruz — equality by value, immutable, deconstruction desteği.
/// </summary>
public sealed record Error(string Code, string Message, ErrorType Type = ErrorType.Failure)
{
    // Sık kullanılan hata fabrikaları — her yerde new Error(...) yazmak yerine
    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    /// <summary>Hata olmadığını temsil eden sentinel value.</summary>
    public static readonly Error None = new(string.Empty, string.Empty);
}

/// <summary>
/// Hata türü — endpoint'lerde HTTP status code mapping için kullanılacak.
/// </summary>
public enum ErrorType
{
    Failure = 0,      // 500
    NotFound = 1,     // 404
    Validation = 2,   // 400
    Conflict = 3,     // 409
    Unauthorized = 4  // 401
}
