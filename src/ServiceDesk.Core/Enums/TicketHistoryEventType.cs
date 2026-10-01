namespace ServiceDesk.Core.Enums;

public enum TicketHistoryEventType
{
    Created = 0,
    Assigned = 1,
    Reassigned = 2,
    Unassigned = 3,
    PriorityChanged = 4,
    StatusChanged = 5,
    Resolved = 6,
    ResolutionRejected = 7,
    Closed = 8
}
