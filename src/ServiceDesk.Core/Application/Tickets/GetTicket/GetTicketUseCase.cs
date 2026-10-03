namespace ServiceDesk.Core.Application.Tickets.GetTicket;

public sealed class GetTicketUseCase(IGetTicketQuery query)
{
    public Task<GetTicketResult?> ExecuteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Ticket ID cannot be empty.", nameof(id));
        }

        cancellationToken.ThrowIfCancellationRequested();

        return query.FindAsync(id, cancellationToken);
    }
}
