namespace ServiceDesk.Core.Application.Tickets.Assignment;

public enum TicketAssignmentPersistenceOutcome
{
    Persisted = 0,
    ConcurrencyConflict = 1
}
