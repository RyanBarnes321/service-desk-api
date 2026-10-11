using ServiceDesk.Core.Enums;

namespace ServiceDesk.Api.Contracts.Tickets;

public sealed record TicketHistoryResponse(
    Guid Id,
    Guid TicketId,
    Guid? PerformedByUserId,
    TicketHistoryEventType EventType,
    string? OldValue,
    string? NewValue,
    DateTimeOffset CreatedAt);
