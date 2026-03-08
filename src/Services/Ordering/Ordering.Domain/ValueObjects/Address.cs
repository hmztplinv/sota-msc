namespace Ordering.Domain.ValueObjects;

public record Address
{
    public string FirstName { get; } = default!;
    public string LastName { get; } = default!;
    public string? EmailAddress { get; } = default!;
    public string AddressLine { get; } = default!;
    public string Country { get; } = default!;
    public string State { get; } = default!;
    public string ZipCode { get; } = default!;

    protected Address() { }

    private Address(string firstName, string lastName, string emailAddress,
        string addressLine, string country, string state, string zipCode)
    {
        FirstName = firstName;
        LastName = lastName;
        EmailAddress = emailAddress;
        AddressLine = addressLine;
        Country = country;
        State = state;
        ZipCode = zipCode;
    }

    public static Address Of(string firstName, string lastName, string emailAddress,
        string addressLine, string country, string state, string zipCode)
    {
        if (string.IsNullOrWhiteSpace(firstName))
            throw new DomainException("Address FirstName cannot be empty.");

        if (string.IsNullOrWhiteSpace(lastName))
            throw new DomainException("Address LastName cannot be empty.");

        if (string.IsNullOrWhiteSpace(emailAddress))
            throw new DomainException("Address EmailAddress cannot be empty.");

        if (string.IsNullOrWhiteSpace(addressLine))
            throw new DomainException("Address AddressLine cannot be empty.");

        return new Address(firstName, lastName, emailAddress, addressLine, country, state, zipCode);
    }
}
