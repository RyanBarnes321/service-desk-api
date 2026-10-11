using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.Tickets.History;

public sealed record TicketHistoryItem(
    Guid Id,
    Guid TicketId,
    Guid? PerformedByUserId,
    TicketHistoryEventType EventType,
    string? OldValue,
    string? NewValue,
    DateTimeOffset CreatedAt);
