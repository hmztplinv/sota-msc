namespace Ordering.Domain.Abstractions;

public interface IDomainEvent : MediatR.INotification
{
    Guid EventId => Guid.NewGuid();
    DateTime OccurredOn => DateTime.UtcNow;
    string EventType => GetType().AssemblyQualifiedName!;
}
