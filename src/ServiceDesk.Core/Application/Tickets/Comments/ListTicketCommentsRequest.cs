namespace ServiceDesk.Core.Application.Tickets.Comments;

public sealed record ListTicketCommentsRequest(int Page = 1, int PageSize = 20)
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 100;
}
