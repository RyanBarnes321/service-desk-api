using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.Tickets.Assignment;

public sealed record TicketAssignmentDetails(
    Guid Id,
    TicketStatus Status,
    Guid? AssignedTechnicianId,
    DateTimeOffset UpdatedAt);
