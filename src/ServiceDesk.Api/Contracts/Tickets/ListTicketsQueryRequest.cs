using ServiceDesk.Core.Application.Tickets.ListTickets;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Api.Contracts.Tickets;

public sealed class ListTicketsQueryRequest
{
    public TicketStatus? Status { get; init; }

    public TicketPriority? Priority { get; init; }

    public TicketCategory? Category { get; init; }

    public Guid? AssignedTechnicianId { get; init; }

    public Guid? CreatedByUserId { get; init; }

    public DateTimeOffset? CreatedFrom { get; init; }

    public DateTimeOffset? CreatedTo { get; init; }

    public string? Search { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public TicketSortField? SortField { get; init; }

    public TicketSortDirection? SortDirection { get; init; }
}
