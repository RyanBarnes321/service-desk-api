namespace ServiceDesk.Core.Application.Tickets.Assignment;

public enum TicketMutationPersistenceOutcome
{
    Persisted = 0,
    ConcurrencyConflict = 1
}
