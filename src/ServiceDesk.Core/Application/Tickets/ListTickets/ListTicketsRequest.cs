using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.Tickets.ListTickets;

public sealed record ListTicketsRequest(
    TicketStatus? Status = null,
    TicketPriority? Priority = null,
    TicketCategory? Category = null,
    Guid? AssignedTechnicianId = null,
    Guid? CreatedByUserId = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20,
    TicketSortField SortField = TicketSortField.CreatedAt,
    TicketSortDirection SortDirection = TicketSortDirection.Desc)
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 100;
}
