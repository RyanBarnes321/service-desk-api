using ServiceDesk.Core.Entities;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Tests.Entities;

public class TicketTests
{
    private static readonly Guid CreatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TechnicianId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OtherTechnicianId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTimeOffset CreatedAt = new(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);

    [Fact]
    public void Create_WithValidValues_CreatesOpenUnassignedTicket()
    {
        const string title = " Printer issue ";
        const string description = " Paper jams during every print job. ";

        var ticket = Ticket.Create(
            title,
            description,
            TicketCategory.Hardware,
            TicketPriority.High,
            CreatorId,
            CreatedAt);

        Assert.NotEqual(Guid.Empty, ticket.Id);
        Assert.Equal(title, ticket.Title);
        Assert.Equal(description, ticket.Description);
        Assert.Equal(TicketCategory.Hardware, ticket.Category);
        Assert.Equal(TicketPriority.High, ticket.Priority);
        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Equal(CreatorId, ticket.CreatedByUserId);
        Assert.Null(ticket.AssignedTechnicianId);
        Assert.Null(ticket.ResolutionSummary);
        Assert.Equal(CreatedAt, ticket.CreatedAt);
        Assert.Equal(CreatedAt, ticket.UpdatedAt);
        Assert.Null(ticket.ResolvedAt);
        Assert.Null(ticket.ClosedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithInvalidTitle_ThrowsArgumentException(string? title)
    {
        Assert.ThrowsAny<ArgumentException>(() => Ticket.Create(
            title!,
            "Description",
            TicketCategory.Hardware,
            TicketPriority.Medium,
            CreatorId,
            CreatedAt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithInvalidDescription_ThrowsArgumentException(string? description)
    {
        Assert.ThrowsAny<ArgumentException>(() => Ticket.Create(
            "Title",
            description!,
            TicketCategory.Hardware,
            TicketPriority.Medium,
            CreatorId,
            CreatedAt));
    }

    [Fact]
    public void Create_WithUndefinedCategory_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Ticket.Create(
            "Title",
            "Description",
            (TicketCategory)999,
            TicketPriority.Medium,
            CreatorId,
            CreatedAt));
    }

    [Fact]
    public void Create_WithUndefinedPriority_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Ticket.Create(
            "Title",
            "Description",
            TicketCategory.Hardware,
            (TicketPriority)999,
            CreatorId,
            CreatedAt));
    }

    [Fact]
    public void Create_WithEmptyCreatorId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Ticket.Create(
            "Title",
            "Description",
            TicketCategory.Hardware,
            TicketPriority.Medium,
            Guid.Empty,
            CreatedAt));
    }

    [Fact]
    public void Assign_FromOpen_AssignsTechnicianAndChangesStatus()
    {
        var ticket = CreateTicket();
        var occurredAt = NextTime(ticket);

        ticket.Assign(TechnicianId, occurredAt);

        Assert.Equal<Guid?>(TechnicianId, ticket.AssignedTechnicianId);
        Assert.Equal(TicketStatus.Assigned, ticket.Status);
        Assert.Equal(occurredAt, ticket.UpdatedAt);
    }

    [Fact]
    public void Assign_WithEmptyTechnicianId_ThrowsArgumentException()
    {
        var ticket = CreateTicket();

        Assert.Throws<ArgumentException>(() => ticket.Assign(Guid.Empty, NextTime(ticket)));
    }

    [Fact]
    public void Assign_WhenAlreadyAssigned_ThrowsInvalidOperationException()
    {
        var ticket = CreateTicketInStatus(TicketStatus.Assigned);

        Assert.Throws<InvalidOperationException>(() => ticket.Assign(OtherTechnicianId, NextTime(ticket)));
    }

    [Theory]
    [InlineData(TicketStatus.Assigned)]
    [InlineData(TicketStatus.InProgress)]
    [InlineData(TicketStatus.Waiting)]
    public void Reassign_FromAllowedStatus_AssignsNewTechnicianAndResetsStatus(TicketStatus initialStatus)
    {
        var ticket = CreateTicketInStatus(initialStatus);
        var occurredAt = NextTime(ticket);

        ticket.Reassign(OtherTechnicianId, occurredAt);

        Assert.Equal<Guid?>(OtherTechnicianId, ticket.AssignedTechnicianId);
        Assert.Equal(TicketStatus.Assigned, ticket.Status);
        Assert.Equal(occurredAt, ticket.UpdatedAt);
    }

    [Fact]
    public void Reassign_ToSameTechnician_ThrowsInvalidOperationException()
    {
        var ticket = CreateTicketInStatus(TicketStatus.Assigned);

        Assert.Throws<InvalidOperationException>(() => ticket.Reassign(TechnicianId, NextTime(ticket)));
    }

    [Fact]
    public void Reassign_WithEmptyTechnicianId_ThrowsArgumentException()
    {
        var ticket = CreateTicketInStatus(TicketStatus.Assigned);

        Assert.Throws<ArgumentException>(() => ticket.Reassign(Guid.Empty, NextTime(ticket)));
    }

    [Theory]
    [InlineData(TicketStatus.Open)]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public void Reassign_FromInvalidStatus_ThrowsInvalidOperationException(TicketStatus status)
    {
        var ticket = CreateTicketInStatus(status);

        Assert.Throws<InvalidOperationException>(() => ticket.Reassign(OtherTechnicianId, NextTime(ticket)));
    }

    [Theory]
    [InlineData(TicketStatus.Assigned)]
    [InlineData(TicketStatus.InProgress)]
    [InlineData(TicketStatus.Waiting)]
    public void Unassign_FromAllowedStatus_ClearsTechnicianAndReopensTicket(TicketStatus initialStatus)
    {
        var ticket = CreateTicketInStatus(initialStatus);
        var occurredAt = NextTime(ticket);

        ticket.Unassign(occurredAt);

        Assert.Null(ticket.AssignedTechnicianId);
        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Equal(occurredAt, ticket.UpdatedAt);
    }

    [Fact]
    public void Unassign_WhenOpenAndUnassigned_ThrowsInvalidOperationException()
    {
        var ticket = CreateTicket();

        Assert.Throws<InvalidOperationException>(() => ticket.Unassign(NextTime(ticket)));
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public void Unassign_FromFinalStatus_ThrowsInvalidOperationException(TicketStatus status)
    {
        var ticket = CreateTicketInStatus(status);

        Assert.Throws<InvalidOperationException>(() => ticket.Unassign(NextTime(ticket)));
    }

    [Fact]
    public void StartWork_FromAssigned_ChangesStatusToInProgress()
    {
        var ticket = CreateTicketInStatus(TicketStatus.Assigned);
        var occurredAt = NextTime(ticket);

        ticket.StartWork(occurredAt);

        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Equal(occurredAt, ticket.UpdatedAt);
    }

    [Fact]
    public void StartWork_FromOpen_ThrowsInvalidOperationException()
    {
        var ticket = CreateTicket();

        Assert.Throws<InvalidOperationException>(() => ticket.StartWork(NextTime(ticket)));
    }

    [Fact]
    public void Wait_FromInProgress_ChangesStatusToWaiting()
    {
        var ticket = CreateTicketInStatus(TicketStatus.InProgress);
        var occurredAt = NextTime(ticket);

        ticket.Wait(occurredAt);

        Assert.Equal(TicketStatus.Waiting, ticket.Status);
        Assert.Equal(occurredAt, ticket.UpdatedAt);
    }

    [Fact]
    public void Wait_FromAssigned_ThrowsInvalidOperationException()
    {
        var ticket = CreateTicketInStatus(TicketStatus.Assigned);

        Assert.Throws<InvalidOperationException>(() => ticket.Wait(NextTime(ticket)));
    }

    [Fact]
    public void Resume_FromWaiting_ChangesStatusToInProgress()
    {
        var ticket = CreateTicketInStatus(TicketStatus.Waiting);
        var occurredAt = NextTime(ticket);

        ticket.Resume(occurredAt);

        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Equal(occurredAt, ticket.UpdatedAt);
    }

    [Fact]
    public void Resume_FromInProgress_ThrowsInvalidOperationException()
    {
        var ticket = CreateTicketInStatus(TicketStatus.InProgress);

        Assert.Throws<InvalidOperationException>(() => ticket.Resume(NextTime(ticket)));
    }

    [Fact]
    public void Resolve_FromInProgress_SetsResolutionDetails()
    {
        var ticket = CreateTicketInStatus(TicketStatus.InProgress);
        var occurredAt = NextTime(ticket);
        const string summary = " Replaced the damaged component. ";

        ticket.Resolve(summary, occurredAt);

        Assert.Equal(TicketStatus.Resolved, ticket.Status);
        Assert.Equal(summary, ticket.ResolutionSummary);
        Assert.Equal<DateTimeOffset?>(occurredAt, ticket.ResolvedAt);
        Assert.Equal(occurredAt, ticket.UpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_WithInvalidSummary_ThrowsArgumentException(string? summary)
    {
        var ticket = CreateTicketInStatus(TicketStatus.InProgress);

        Assert.ThrowsAny<ArgumentException>(() => ticket.Resolve(summary!, NextTime(ticket)));
    }

    [Fact]
    public void Resolve_WithOverLimitSummary_ThrowsArgumentException()
    {
        var ticket = CreateTicketInStatus(TicketStatus.InProgress);

        var exception = Assert.Throws<ArgumentException>(() => ticket.Resolve(
            new string('x', Ticket.MaximumResolutionSummaryLength + 1),
            NextTime(ticket)));

        Assert.Equal("resolutionSummary", exception.ParamName);
        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Null(ticket.ResolutionSummary);
    }

    [Theory]
    [InlineData(TicketStatus.Assigned)]
    [InlineData(TicketStatus.Waiting)]
    [InlineData(TicketStatus.Resolved)]
    public void Resolve_FromInvalidStatus_ThrowsInvalidOperationException(TicketStatus status)
    {
        var ticket = CreateTicketInStatus(status);

        Assert.Throws<InvalidOperationException>(() => ticket.Resolve("Resolution", NextTime(ticket)));
    }

    [Fact]
    public void RejectResolution_FromResolved_ReopensAndClearsResolution()
    {
        var ticket = CreateTicketInStatus(TicketStatus.Resolved);
        var occurredAt = NextTime(ticket);

        ticket.RejectResolution(occurredAt);

        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Null(ticket.ResolutionSummary);
        Assert.Null(ticket.ResolvedAt);
        Assert.Equal<Guid?>(TechnicianId, ticket.AssignedTechnicianId);
        Assert.Equal(occurredAt, ticket.UpdatedAt);
    }

    [Theory]
    [InlineData(TicketStatus.Open)]
    [InlineData(TicketStatus.Closed)]
    public void RejectResolution_FromInvalidStatus_ThrowsInvalidOperationException(TicketStatus status)
    {
        var ticket = CreateTicketInStatus(status);

        Assert.Throws<InvalidOperationException>(() => ticket.RejectResolution(NextTime(ticket)));
    }

    [Fact]
    public void Close_FromResolved_ClosesAndRetainsResolutionDetails()
    {
        var ticket = CreateTicketInStatus(TicketStatus.Resolved);
        var resolutionSummary = ticket.ResolutionSummary;
        var resolvedAt = ticket.ResolvedAt;
        var occurredAt = NextTime(ticket);

        ticket.Close(occurredAt);

        Assert.Equal(TicketStatus.Closed, ticket.Status);
        Assert.Equal<DateTimeOffset?>(occurredAt, ticket.ClosedAt);
        Assert.Equal(resolutionSummary, ticket.ResolutionSummary);
        Assert.Equal(resolvedAt, ticket.ResolvedAt);
        Assert.Equal(occurredAt, ticket.UpdatedAt);
    }

    [Theory]
    [InlineData(TicketStatus.InProgress)]
    [InlineData(TicketStatus.Closed)]
    public void Close_FromInvalidStatus_ThrowsInvalidOperationException(TicketStatus status)
    {
        var ticket = CreateTicketInStatus(status);

        Assert.Throws<InvalidOperationException>(() => ticket.Close(NextTime(ticket)));
    }

    [Fact]
    public void ClosedTicket_RejectsEveryAvailableMutation()
    {
        var ticket = CreateTicketInStatus(TicketStatus.Closed);
        var occurredAt = NextTime(ticket);

        Assert.Throws<InvalidOperationException>(() => ticket.Assign(OtherTechnicianId, occurredAt));
        Assert.Throws<InvalidOperationException>(() => ticket.Reassign(OtherTechnicianId, occurredAt));
        Assert.Throws<InvalidOperationException>(() => ticket.Unassign(occurredAt));
        Assert.Throws<InvalidOperationException>(() => ticket.StartWork(occurredAt));
        Assert.Throws<InvalidOperationException>(() => ticket.Wait(occurredAt));
        Assert.Throws<InvalidOperationException>(() => ticket.Resume(occurredAt));
        Assert.Throws<InvalidOperationException>(() => ticket.Resolve("Another resolution", occurredAt));
        Assert.Throws<InvalidOperationException>(() => ticket.Close(occurredAt));
        Assert.Throws<InvalidOperationException>(() => ticket.RejectResolution(occurredAt));
        Assert.Throws<InvalidOperationException>(() => ticket.ChangePriority(TicketPriority.High, occurredAt));
    }

    [Theory]
    [InlineData(TicketStatus.Assigned)]
    [InlineData(TicketStatus.InProgress)]
    [InlineData(TicketStatus.Waiting)]
    public void ChangePriority_FromAllowedStatus_ChangesPriority(TicketStatus status)
    {
        var ticket = CreateTicketInStatus(status);
        var occurredAt = NextTime(ticket);

        ticket.ChangePriority(TicketPriority.High, occurredAt);

        Assert.Equal(TicketPriority.High, ticket.Priority);
        Assert.Equal(occurredAt, ticket.UpdatedAt);
    }

    [Fact]
    public void ChangePriority_ToSamePriority_ThrowsInvalidOperationException()
    {
        var ticket = CreateTicketInStatus(TicketStatus.Assigned);

        Assert.Throws<InvalidOperationException>(() =>
            ticket.ChangePriority(TicketPriority.Medium, NextTime(ticket)));
    }

    [Fact]
    public void ChangePriority_ToUndefinedPriority_ThrowsArgumentOutOfRangeException()
    {
        var ticket = CreateTicketInStatus(TicketStatus.Assigned);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ticket.ChangePriority((TicketPriority)999, NextTime(ticket)));
    }

    [Theory]
    [InlineData(TicketStatus.Open)]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public void ChangePriority_FromInvalidStatus_ThrowsInvalidOperationException(TicketStatus status)
    {
        var ticket = CreateTicketInStatus(status);

        Assert.Throws<InvalidOperationException>(() =>
            ticket.ChangePriority(TicketPriority.High, NextTime(ticket)));
    }

    [Fact]
    public void Mutation_WithTimestampEqualToUpdatedAt_Succeeds()
    {
        var ticket = CreateTicket();

        ticket.Assign(TechnicianId, ticket.UpdatedAt);

        Assert.Equal(TicketStatus.Assigned, ticket.Status);
        Assert.Equal(CreatedAt, ticket.UpdatedAt);
    }

    [Fact]
    public void Mutations_WithTimestampEarlierThanUpdatedAt_AreRejected()
    {
        var openTicket = CreateTicket();
        var assignedTicket = CreateTicketInStatus(TicketStatus.Assigned);
        var resolvedTicket = CreateTicketInStatus(TicketStatus.Resolved);
        var priorityTicket = CreateTicketInStatus(TicketStatus.Assigned);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            openTicket.Assign(TechnicianId, openTicket.UpdatedAt.AddTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            assignedTicket.StartWork(assignedTicket.UpdatedAt.AddTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            resolvedTicket.Close(resolvedTicket.UpdatedAt.AddTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            priorityTicket.ChangePriority(TicketPriority.High, priorityTicket.UpdatedAt.AddTicks(-1)));
    }

    private static Ticket CreateTicket()
    {
        return Ticket.Create(
            "Printer issue",
            "Paper jams during every print job.",
            TicketCategory.Hardware,
            TicketPriority.Medium,
            CreatorId,
            CreatedAt);
    }

    private static Ticket CreateTicketInStatus(TicketStatus status)
    {
        var ticket = CreateTicket();

        if (status == TicketStatus.Open)
        {
            return ticket;
        }

        ticket.Assign(TechnicianId, NextTime(ticket));

        if (status == TicketStatus.Assigned)
        {
            return ticket;
        }

        ticket.StartWork(NextTime(ticket));

        if (status == TicketStatus.InProgress)
        {
            return ticket;
        }

        if (status == TicketStatus.Waiting)
        {
            ticket.Wait(NextTime(ticket));
            return ticket;
        }

        ticket.Resolve("Replaced the damaged component.", NextTime(ticket));

        if (status == TicketStatus.Resolved)
        {
            return ticket;
        }

        if (status == TicketStatus.Closed)
        {
            ticket.Close(NextTime(ticket));
            return ticket;
        }

        throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported test status.");
    }

    private static DateTimeOffset NextTime(Ticket ticket)
    {
        return ticket.UpdatedAt.AddMinutes(1);
    }
}
