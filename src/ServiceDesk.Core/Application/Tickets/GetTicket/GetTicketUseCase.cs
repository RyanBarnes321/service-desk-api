using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Application.Tickets;

namespace ServiceDesk.Core.Application.Tickets.GetTicket;

public sealed class GetTicketUseCase(IGetTicketQuery query)
{
    public Task<GetTicketResult?> ExecuteAsync(
        Guid id,
        RequestActor actor,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Ticket ID cannot be empty.", nameof(id));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var visibility = TicketVisibilityScope.For(actor);

        return query.FindAsync(id, visibility, cancellationToken);
    }
}
