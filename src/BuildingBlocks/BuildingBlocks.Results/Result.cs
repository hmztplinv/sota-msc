namespace BuildingBlocks.Results;

/// <summary>
/// Değer döndürmeyen işlemler için Result.
/// Örnek: DeleteProduct → Result (başarılı mı, değil mi?)
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        // Invariant: Success ise error None olmalı, Failure ise error olmamalı None
        if (isSuccess && error != Error.None)
            throw new InvalidOperationException("Success result cannot have an error.");
        if (!isSuccess && error == Error.None)
            throw new InvalidOperationException("Failure result must have an error.");

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);

    // Implicit conversion — Error'dan otomatik Failure Result oluştur
    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>
/// Değer döndüren işlemler için Result{T}.
/// Örnek: GetProductById → Result{ProductResponse}
/// </summary>
public class Result<T> : Result
{
    private readonly T? _value;

    private Result(T value) : base(true, Error.None) => _value = value;
    private Result(Error error) : base(false, error) => _value = default;

    /// <summary>
    /// Başarılıysa değeri döndürür. Başarısızsa InvalidOperationException fırlatır.
    /// Bu exception "flow control" değil — programcı hatasını yakalar (defensive).
    /// </summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value on a failure result.");

    public static Result<T> Success(T value) => new(value);
    public new static Result<T> Failure(Error error) => new(error);

    // Implicit conversions — temiz kullanım için
    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}
