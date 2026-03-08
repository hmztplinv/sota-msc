namespace Ordering.Domain.ValueObjects;

public record OrderName
{
    private const int MaxLength = 100;
    public string Value { get; }

    private OrderName(string value) => Value = value;

    public static OrderName Of(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("OrderName cannot be empty.");

        if (value.Length > MaxLength)
            throw new DomainException($"OrderName cannot exceed {MaxLength} characters.");

        return new OrderName(value);
    }
}
