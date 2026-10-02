using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Entities;

public class TicketHistory
{
    private TicketHistory(
        Guid id,
        Guid ticketId,
        Guid? performedByUserId,
        TicketHistoryEventType eventType,
        string? oldValue,
        string? newValue,
        DateTimeOffset createdAt)
    {
        Id = id;
        TicketId = ticketId;
        PerformedByUserId = performedByUserId;
        EventType = eventType;
        OldValue = oldValue;
        NewValue = newValue;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public Guid TicketId { get; }

    public Guid? PerformedByUserId { get; }

    public TicketHistoryEventType EventType { get; }

    public string? OldValue { get; }

    public string? NewValue { get; }

    public DateTimeOffset CreatedAt { get; }

    public static TicketHistory Create(
        Guid ticketId,
        Guid? performedByUserId,
        TicketHistoryEventType eventType,
        string? oldValue,
        string? newValue,
        DateTimeOffset createdAt)
    {
        if (ticketId == Guid.Empty)
        {
            throw new ArgumentException("Ticket ID cannot be empty.", nameof(ticketId));
        }

        if (performedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Performer user ID cannot be empty.", nameof(performedByUserId));
        }

        if (!Enum.IsDefined(eventType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(eventType),
                eventType,
                "Event type must be a defined value.");
        }

        return new TicketHistory(
            Guid.NewGuid(),
            ticketId,
            performedByUserId,
            eventType,
            oldValue,
            newValue,
            createdAt);
    }
}
