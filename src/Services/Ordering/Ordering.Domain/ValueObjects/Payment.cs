namespace Ordering.Domain.ValueObjects;

public record Payment
{
    public string CardName { get; } = default!;
    public string CardNumber { get; } = default!;
    public string Expiration { get; } = default!;
    public string CVV { get; } = default!;
    public int PaymentMethod { get; }

    protected Payment() { }

    private Payment(string cardName, string cardNumber, string expiration, string cvv, int paymentMethod)
    {
        CardName = cardName;
        CardNumber = cardNumber;
        Expiration = expiration;
        CVV = cvv;
        PaymentMethod = paymentMethod;
    }

    public static Payment Of(string cardName, string cardNumber, string expiration, string cvv, int paymentMethod)
    {
        if (string.IsNullOrWhiteSpace(cardName))
            throw new DomainException("Payment CardName cannot be empty.");

        if (string.IsNullOrWhiteSpace(cardNumber) || cardNumber.Length < 13)
            throw new DomainException("Payment CardNumber is invalid.");

        if (string.IsNullOrWhiteSpace(expiration))
            throw new DomainException("Payment Expiration cannot be empty.");

        if (string.IsNullOrWhiteSpace(cvv) || cvv.Length < 3)
            throw new DomainException("Payment CVV is invalid.");

        return new Payment(cardName, cardNumber, expiration, cvv, paymentMethod);
    }
}
