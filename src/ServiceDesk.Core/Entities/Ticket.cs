using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Entities;

public class Ticket
{
    private Ticket(
        Guid id,
        string title,
        string description,
        TicketCategory category,
        TicketPriority priority,
        Guid createdByUserId,
        DateTimeOffset createdAt)
    {
        Id = id;
        Title = title;
        Description = description;
        Category = category;
        Priority = priority;
        Status = TicketStatus.Open;
        CreatedByUserId = createdByUserId;
        AssignedTechnicianId = null;
        ResolutionSummary = null;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        ResolvedAt = null;
        ClosedAt = null;
    }

    public Guid Id { get; private set; }

    public string Title { get; private set; }

    public string Description { get; private set; }

    public TicketCategory Category { get; private set; }

    public TicketPriority Priority { get; private set; }

    public TicketStatus Status { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public Guid? AssignedTechnicianId { get; private set; }

    public string? ResolutionSummary { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public static Ticket Create(
        string title,
        string description,
        TicketCategory category,
        TicketPriority priority,
        Guid createdByUserId,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        EnsureCategoryIsDefined(category);
        EnsurePriorityIsDefined(priority);

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("Creator ID cannot be empty.", nameof(createdByUserId));
        }

        return new Ticket(
            Guid.NewGuid(),
            title,
            description,
            category,
            priority,
            createdByUserId,
            createdAt);
    }

    public void Assign(Guid technicianId, DateTimeOffset occurredAt)
    {
        EnsureTechnicianIdIsNotEmpty(technicianId, nameof(technicianId));
        EnsureChronology(occurredAt);

        if (Status != TicketStatus.Open || AssignedTechnicianId is not null)
        {
            throw new InvalidOperationException("Only an unassigned Open ticket can be assigned.");
        }

        AssignedTechnicianId = technicianId;
        Status = TicketStatus.Assigned;
        UpdatedAt = occurredAt;
    }

    public void Reassign(Guid newTechnicianId, DateTimeOffset occurredAt)
    {
        EnsureTechnicianIdIsNotEmpty(newTechnicianId, nameof(newTechnicianId));
        EnsureChronology(occurredAt);

        if (AssignedTechnicianId is null || !IsActiveAssignmentStatus())
        {
            throw new InvalidOperationException(
                "Only an assigned ticket in Assigned, InProgress, or Waiting status can be reassigned.");
        }

        if (AssignedTechnicianId == newTechnicianId)
        {
            throw new InvalidOperationException("The ticket is already assigned to this technician.");
        }

        AssignedTechnicianId = newTechnicianId;
        Status = TicketStatus.Assigned;
        UpdatedAt = occurredAt;
    }

    public void Unassign(DateTimeOffset occurredAt)
    {
        EnsureChronology(occurredAt);

        if (AssignedTechnicianId is null || !IsActiveAssignmentStatus())
        {
            throw new InvalidOperationException(
                "Only an assigned ticket in Assigned, InProgress, or Waiting status can be unassigned.");
        }

        AssignedTechnicianId = null;
        Status = TicketStatus.Open;
        UpdatedAt = occurredAt;
    }

    public void StartWork(DateTimeOffset occurredAt)
    {
        EnsureChronology(occurredAt);
        EnsureStatus(TicketStatus.Assigned, nameof(StartWork));

        Status = TicketStatus.InProgress;
        UpdatedAt = occurredAt;
    }

    public void Wait(DateTimeOffset occurredAt)
    {
        EnsureChronology(occurredAt);
        EnsureStatus(TicketStatus.InProgress, nameof(Wait));

        Status = TicketStatus.Waiting;
        UpdatedAt = occurredAt;
    }

    public void Resume(DateTimeOffset occurredAt)
    {
        EnsureChronology(occurredAt);
        EnsureStatus(TicketStatus.Waiting, nameof(Resume));

        Status = TicketStatus.InProgress;
        UpdatedAt = occurredAt;
    }

    public void Resolve(string resolutionSummary, DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resolutionSummary);
        EnsureChronology(occurredAt);
        EnsureStatus(TicketStatus.InProgress, nameof(Resolve));

        Status = TicketStatus.Resolved;
        ResolutionSummary = resolutionSummary;
        ResolvedAt = occurredAt;
        UpdatedAt = occurredAt;
    }

    public void Close(DateTimeOffset occurredAt)
    {
        EnsureChronology(occurredAt);
        EnsureStatus(TicketStatus.Resolved, nameof(Close));

        Status = TicketStatus.Closed;
        ClosedAt = occurredAt;
        UpdatedAt = occurredAt;
    }

    public void RejectResolution(DateTimeOffset occurredAt)
    {
        EnsureChronology(occurredAt);
        EnsureStatus(TicketStatus.Resolved, nameof(RejectResolution));

        Status = TicketStatus.InProgress;
        ResolutionSummary = null;
        ResolvedAt = null;
        UpdatedAt = occurredAt;
    }

    public void ChangePriority(TicketPriority newPriority, DateTimeOffset occurredAt)
    {
        EnsurePriorityIsDefined(newPriority);
        EnsureChronology(occurredAt);

        if (!IsActiveAssignmentStatus())
        {
            throw new InvalidOperationException(
                "Priority can only be changed while a ticket is Assigned, InProgress, or Waiting.");
        }

        if (Priority == newPriority)
        {
            throw new InvalidOperationException("The ticket already has this priority.");
        }

        Priority = newPriority;
        UpdatedAt = occurredAt;
    }

    private bool IsActiveAssignmentStatus()
    {
        return Status is TicketStatus.Assigned or TicketStatus.InProgress or TicketStatus.Waiting;
    }

    private void EnsureChronology(DateTimeOffset occurredAt)
    {
        if (occurredAt < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(occurredAt),
                occurredAt,
                "The occurrence time cannot be earlier than the last update.");
        }
    }

    private void EnsureStatus(TicketStatus requiredStatus, string operation)
    {
        if (Status != requiredStatus)
        {
            throw new InvalidOperationException(
                $"{operation} requires the ticket to be in {requiredStatus} status.");
        }
    }

    private static void EnsureCategoryIsDefined(TicketCategory category)
    {
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category), category, "Category must be a defined value.");
        }
    }

    private static void EnsurePriorityIsDefined(TicketPriority priority)
    {
        if (!Enum.IsDefined(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority), priority, "Priority must be a defined value.");
        }
    }

    private static void EnsureTechnicianIdIsNotEmpty(Guid technicianId, string parameterName)
    {
        if (technicianId == Guid.Empty)
        {
            throw new ArgumentException("Technician ID cannot be empty.", parameterName);
        }
    }
}
