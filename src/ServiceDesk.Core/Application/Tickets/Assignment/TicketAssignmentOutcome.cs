namespace ServiceDesk.Core.Application.Tickets.Assignment;

public enum TicketAssignmentOutcome
{
    Success = 0,
    Forbidden = 1,
    TicketNotFound = 2,
    TargetUnavailable = 3,
    InvalidState = 4,
    ConcurrencyConflict = 5
}
