using ServiceDesk.Core.Enums;

namespace ServiceDesk.Api.Contracts.Tickets;

public sealed record TicketAssignmentResponse(
    Guid Id,
    TicketStatus Status,
    Guid? AssignedTechnicianId,
    DateTimeOffset UpdatedAt);
