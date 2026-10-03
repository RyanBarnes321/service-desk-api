namespace ServiceDesk.Core.Application.Tickets.GetTicket;

public interface IGetTicketQuery
{
    Task<GetTicketResult?> FindAsync(Guid id, CancellationToken cancellationToken);
}
