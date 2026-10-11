using ServiceDesk.Api.Contracts.Tickets;
using ServiceDesk.Api.Security;
using ServiceDesk.Core.Application.Tickets.Assignment;
using ServiceDesk.Core.Application.Tickets.Comments;
using ServiceDesk.Core.Application.Tickets.CreateTicket;
using ServiceDesk.Core.Application.Tickets.GetTicket;
using ServiceDesk.Core.Application.Tickets.History;
using ServiceDesk.Core.Application.Tickets.ListTickets;
using ServiceDesk.Core.Application.Tickets.ResolutionReview;
using ServiceDesk.Core.Application.Tickets.TechnicalOperations;

namespace ServiceDesk.Api.Endpoints;

public static class TicketEndpoints
{
    private const string GetTicketRouteName = "GetTicket";

    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var tickets = endpoints.MapGroup("/api/tickets");

        Document(tickets.MapPost("/", CreateAsync), "Create a ticket",
                "Creates an Open ticket for the authenticated actor and records its Created history event. " +
                "Example enum values: category \"Software\", priority \"High\".")
            .Produces<CreateTicketResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);
        Document(tickets.MapGet("/", ListAsync), "List visible tickets",
                "Filters, searches, sorts, and paginates tickets. Employees see only tickets they created; " +
                "Technicians and Administrators see all tickets.")
            .Produces<ListTicketsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);
        Document(tickets.MapGet("/{id:guid}", GetAsync).WithName(GetTicketRouteName), "Get a ticket",
                "Returns one visible ticket. A ticket hidden from an Employee is concealed as 404.")
            .Produces<GetTicketResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);
        Document(tickets.MapPost("/{id:guid}/claim", ClaimAsync), "Claim a ticket",
                "Technician or Administrator self-assignment. Invalid state and concurrent updates return 409.")
            .Produces<TicketAssignmentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        Document(tickets.MapPut("/{id:guid}/assignment", AssignAsync), "Assign or reassign a ticket",
                "Administrator-only assignment to an active Technician. Invalid state and concurrent updates return 409.")
            .Produces<TicketAssignmentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        Document(tickets.MapDelete("/{id:guid}/assignment", UnassignAsync), "Unassign a ticket",
                "Administrator-only unassignment. Invalid state and concurrent updates return 409.")
            .Produces<TicketAssignmentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        Document(tickets.MapPost("/{id:guid}/start", StartWorkAsync), "Start work",
                "Assigned Technician-only transition from Assigned to InProgress.")
            .Produces<TicketTechnicalOperationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        Document(tickets.MapPost("/{id:guid}/wait", WaitAsync), "Place a ticket on hold",
                "Assigned Technician-only transition from InProgress to Waiting.")
            .Produces<TicketTechnicalOperationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        Document(tickets.MapPost("/{id:guid}/resume", ResumeAsync), "Resume work",
                "Assigned Technician-only transition from Waiting to InProgress.")
            .Produces<TicketTechnicalOperationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        Document(tickets.MapPost("/{id:guid}/resolve", ResolveAsync), "Resolve a ticket",
                "Assigned Technician-only resolution from InProgress with a required summary of at most 5,000 characters.")
            .Produces<TicketTechnicalOperationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        Document(tickets.MapPut("/{id:guid}/priority", ChangePriorityAsync), "Change ticket priority",
                "Assigned Technician-only priority change. Same-priority and concurrent updates return 409.")
            .Produces<TicketTechnicalOperationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        Document(tickets.MapPost("/{id:guid}/close", CloseAsync), "Close a resolved ticket",
                "Administrator-only Resolved-to-Closed transition.")
            .Produces<TicketResolutionReviewResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        Document(tickets.MapPost("/{id:guid}/reject-resolution", RejectResolutionAsync), "Reject a resolution",
                "Administrator-only rejection that returns a Resolved ticket to InProgress and preserves the audit trail.")
            .Produces<TicketResolutionReviewResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        Document(tickets.MapPost("/{id:guid}/comments", CreateCommentAsync), "Add a ticket comment",
                "Adds an immutable comment authored by the authenticated actor. Closed tickets return 409; " +
                "visibility is checked first and concealed as 404.")
            .Produces<TicketCommentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        Document(tickets.MapGet("/{id:guid}/comments", ListCommentsAsync), "List ticket comments",
                "Returns visible comments oldest-first with deterministic pagination, including for Closed tickets.")
            .Produces<ListTicketCommentsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);
        Document(tickets.MapGet("/{id:guid}/history", ListHistoryAsync), "List ticket history",
                "Returns the immutable audit trail oldest-first with deterministic pagination. Hidden tickets are 404.")
            .Produces<ListTicketHistoryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);
        tickets.RequireAuthorization(ActiveUserPolicy.Name);

        return endpoints;
    }

    private static RouteHandlerBuilder Document(
        RouteHandlerBuilder builder,
        string summary,
        string description)
    {
        return builder
            .WithTags("Tickets")
            .WithSummary(summary)
            .WithDescription(description)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> CreateAsync(
        CreateTicketRequest request,
        CreateTicketUseCase useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!RequestActorContext.TryGet(httpContext, out var actor))
        {
            return Results.Forbid();
        }

        try
        {
            var command = new CreateTicketCommand(
                request.Title,
                request.Description,
                request.Category,
                request.Priority,
                actor.UserId);
            var result = await useCase.ExecuteAsync(command, cancellationToken);
            var response = new CreateTicketResponse(
                result.Id,
                result.Title,
                result.Description,
                result.Category,
                result.Priority,
                result.Status,
                result.CreatedByUserId,
                result.CreatedAt);

            return Results.CreatedAtRoute(GetTicketRouteName, new { id = response.Id }, response);
        }
        catch (ArgumentException exception) when (IsCreateTicketValidationFailure(exception))
        {
            return InvalidRequest(exception);
        }
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        GetTicketUseCase useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!RequestActorContext.TryGet(httpContext, out var actor))
        {
            return Results.Forbid();
        }

        try
        {
            var result = await useCase.ExecuteAsync(id, actor, cancellationToken);

            if (result is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new GetTicketResponse(
                result.Id,
                result.Title,
                result.Description,
                result.Category,
                result.Priority,
                result.Status,
                result.CreatedByUserId,
                result.AssignedTechnicianId,
                result.ResolutionSummary,
                result.CreatedAt,
                result.UpdatedAt,
                result.ResolvedAt,
                result.ClosedAt));
        }
        catch (ArgumentException exception) when (exception.ParamName is "id")
        {
            return InvalidRequest(exception);
        }
    }

    private static async Task<IResult> ListAsync(
        [AsParameters] ListTicketsQueryRequest request,
        ListTicketsUseCase useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!RequestActorContext.TryGet(httpContext, out var actor))
        {
            return Results.Forbid();
        }

        try
        {
            var applicationRequest = new ListTicketsRequest(
                request.Status,
                request.Priority,
                request.Category,
                request.AssignedTechnicianId,
                // This is client-requested filtering, not user visibility enforcement.
                request.CreatedByUserId,
                request.CreatedFrom,
                request.CreatedTo,
                request.Search,
                request.Page ?? ListTicketsRequest.DefaultPage,
                request.PageSize ?? ListTicketsRequest.DefaultPageSize,
                request.SortField ?? TicketSortField.CreatedAt,
                request.SortDirection ?? TicketSortDirection.Desc);
            var result = await useCase.ExecuteAsync(applicationRequest, actor, cancellationToken);
            var response = new ListTicketsResponse(
                result.Items.Select(item => new ListTicketResponse(
                    item.Id,
                    item.Title,
                    item.Category,
                    item.Priority,
                    item.Status,
                    item.CreatedByUserId,
                    item.AssignedTechnicianId,
                    item.CreatedAt,
                    item.UpdatedAt)).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount,
                result.TotalPages);

            return Results.Ok(response);
        }
        catch (ArgumentException exception) when (IsListTicketsValidationFailure(exception))
        {
            return InvalidRequest(exception);
        }
    }

    private static Task<IResult> ClaimAsync(
        Guid id,
        TicketAssignmentUseCase useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        return ExecuteAssignmentAsync(
            httpContext,
            actor => useCase.ClaimAsync(id, actor, cancellationToken));
    }

    private static Task<IResult> AssignAsync(
        Guid id,
        AssignTicketRequest request,
        TicketAssignmentUseCase useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        return ExecuteAssignmentAsync(
            httpContext,
            actor => useCase.AssignAsync(id, request.TechnicianId, actor, cancellationToken));
    }

    private static Task<IResult> UnassignAsync(
        Guid id,
        TicketAssignmentUseCase useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        return ExecuteAssignmentAsync(
            httpContext,
            actor => useCase.UnassignAsync(id, actor, cancellationToken));
    }

    private static async Task<IResult> ExecuteAssignmentAsync(
        HttpContext httpContext,
        Func<ServiceDesk.Core.Application.Authentication.RequestActor, Task<TicketAssignmentResult>> execute)
    {
        if (!RequestActorContext.TryGet(httpContext, out var actor))
        {
            return Results.Forbid();
        }

        TicketAssignmentResult result;
        try
        {
            result = await execute(actor);
        }
        catch (ArgumentException exception) when (exception.ParamName is "ticketId")
        {
            return InvalidRequest(exception);
        }

        return result.Outcome switch
        {
            TicketAssignmentOutcome.Success => Results.Ok(ToResponse(result.Ticket!)),
            TicketAssignmentOutcome.Forbidden => Results.Forbid(),
            TicketAssignmentOutcome.TicketNotFound => Results.NotFound(),
            TicketAssignmentOutcome.TargetUnavailable => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid technician",
                detail: "The selected technician is unavailable."),
            TicketAssignmentOutcome.InvalidState => AssignmentConflict(
                "The ticket cannot perform this assignment operation in its current state."),
            TicketAssignmentOutcome.ConcurrencyConflict => AssignmentConflict(
                "The ticket was changed by another request. Refresh and try again."),
            _ => throw new InvalidOperationException("Unknown ticket assignment outcome.")
        };
    }

    private static TicketAssignmentResponse ToResponse(TicketAssignmentDetails ticket)
    {
        return new TicketAssignmentResponse(
            ticket.Id,
            ticket.Status,
            ticket.AssignedTechnicianId,
            ticket.UpdatedAt);
    }

    private static IResult AssignmentConflict(string detail)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Ticket assignment conflict",
            detail: detail);
    }

    private static Task<IResult> StartWorkAsync(
        Guid id,
        TicketTechnicalOperationsUseCase useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        return ExecuteTechnicalOperationAsync(
            httpContext,
            actor => useCase.StartWorkAsync(id, actor, cancellationToken));
    }

    private static Task<IResult> WaitAsync(
        Guid id,
        TicketTechnicalOperationsUseCase useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        return ExecuteTechnicalOperationAsync(
            httpContext,
            actor => useCase.WaitAsync(id, actor, cancellationToken));
    }

    private static Task<IResult> ResumeAsync(
        Guid id,
        TicketTechnicalOperationsUseCase useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        return ExecuteTechnicalOperationAsync(
            httpContext,
            actor => useCase.ResumeAsync(id, actor, cancellationToken));
    }

    private static Task<IResult> ResolveAsync(
        Guid id,
        ResolveTicketRequest request,
        TicketTechnicalOperationsUseCase useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        return ExecuteTechnicalOperationAsync(
            httpContext,
            actor => useCase.ResolveAsync(
                id,
                request.ResolutionSummary,
                actor,
                cancellationToken));
    }

    private static Task<IResult> ChangePriorityAsync(
        Guid id,
        ChangeTicketPriorityRequest request,
        TicketTechnicalOperationsUseCase useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        return ExecuteTechnicalOperationAsync(
            httpContext,
            actor => useCase.ChangePriorityAsync(
                id,
                request.Priority,
                actor,
                cancellationToken));
    }

    private static async Task<IResult> ExecuteTechnicalOperationAsync(
        HttpContext httpContext,
        Func<ServiceDesk.Core.Application.Authentication.RequestActor,
            Task<TicketTechnicalOperationResult>> execute)
    {
        if (!RequestActorContext.TryGet(httpContext, out var actor))
        {
            return Results.Forbid();
        }

        TicketTechnicalOperationResult result;
        try
        {
            result = await execute(actor);
        }
        catch (TicketTechnicalOperationValidationException exception)
        {
            return InvalidRequest(exception);
        }

        return result.Outcome switch
        {
            TicketTechnicalOperationOutcome.Success => Results.Ok(
                ToTechnicalResponse(result.Ticket!)),
            TicketTechnicalOperationOutcome.Forbidden => Results.Forbid(),
            TicketTechnicalOperationOutcome.TicketNotFound => Results.NotFound(),
            TicketTechnicalOperationOutcome.InvalidState => TechnicalOperationConflict(
                "The ticket cannot perform this operation in its current state."),
            TicketTechnicalOperationOutcome.ConcurrencyConflict => TechnicalOperationConflict(
                "The ticket was changed by another request. Refresh and try again."),
            _ => throw new InvalidOperationException("Unknown ticket technical-operation outcome.")
        };
    }

    private static TicketTechnicalOperationResponse ToTechnicalResponse(
        TicketTechnicalOperationDetails ticket)
    {
        return new TicketTechnicalOperationResponse(
            ticket.Id,
            ticket.Status,
            ticket.Priority,
            ticket.AssignedTechnicianId,
            ticket.ResolutionSummary,
            ticket.UpdatedAt,
            ticket.ResolvedAt);
    }

    private static IResult TechnicalOperationConflict(string detail)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Ticket operation conflict",
            detail: detail);
    }

    private static Task<IResult> CloseAsync(
        Guid id,
        TicketResolutionReviewUseCase useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        ExecuteResolutionReviewAsync(
            httpContext,
            actor => useCase.CloseAsync(id, actor, cancellationToken));

    private static Task<IResult> RejectResolutionAsync(
        Guid id,
        TicketResolutionReviewUseCase useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        ExecuteResolutionReviewAsync(
            httpContext,
            actor => useCase.RejectResolutionAsync(id, actor, cancellationToken));

    private static async Task<IResult> ExecuteResolutionReviewAsync(
        HttpContext httpContext,
        Func<ServiceDesk.Core.Application.Authentication.RequestActor,
            Task<TicketResolutionReviewResult>> execute)
    {
        if (!RequestActorContext.TryGet(httpContext, out var actor))
        {
            return Results.Forbid();
        }

        TicketResolutionReviewResult result;
        try
        {
            result = await execute(actor);
        }
        catch (TicketResolutionReviewValidationException exception)
        {
            return InvalidRequest(exception);
        }

        return result.Outcome switch
        {
            TicketResolutionReviewOutcome.Success => Results.Ok(ToResolutionReviewResponse(result.Ticket!)),
            TicketResolutionReviewOutcome.Forbidden => Results.Forbid(),
            TicketResolutionReviewOutcome.TicketNotFound => Results.NotFound(),
            TicketResolutionReviewOutcome.InvalidState => ResolutionReviewConflict(
                "The ticket cannot perform this review operation in its current state."),
            TicketResolutionReviewOutcome.ConcurrencyConflict => ResolutionReviewConflict(
                "The ticket was changed by another request. Refresh and try again."),
            _ => throw new InvalidOperationException("Unknown ticket resolution-review outcome.")
        };
    }

    private static TicketResolutionReviewResponse ToResolutionReviewResponse(
        TicketResolutionReviewDetails ticket) =>
        new(
            ticket.Id,
            ticket.Status,
            ticket.AssignedTechnicianId,
            ticket.ResolutionSummary,
            ticket.UpdatedAt,
            ticket.ResolvedAt,
            ticket.ClosedAt);

    private static IResult ResolutionReviewConflict(string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Ticket resolution review conflict",
            detail: detail);

    private static async Task<IResult> CreateCommentAsync(
        Guid id,
        CreateTicketCommentRequest request,
        CreateTicketCommentUseCase useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!RequestActorContext.TryGet(httpContext, out var actor))
        {
            return Results.Forbid();
        }

        CreateTicketCommentResult result;
        try
        {
            result = await useCase.ExecuteAsync(id, request.Content, actor, cancellationToken);
        }
        catch (TicketCommentsValidationException exception)
        {
            return InvalidRequest(exception);
        }

        return result.Outcome switch
        {
            CreateTicketCommentOutcome.Success => Results.Created(
                $"/api/tickets/{id}/comments",
                ToCommentResponse(result.Comment!)),
            CreateTicketCommentOutcome.TicketNotFound => Results.NotFound(),
            CreateTicketCommentOutcome.TicketClosed => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Ticket comment conflict",
                detail: "Closed tickets cannot receive new comments."),
            _ => throw new InvalidOperationException("Unknown create-ticket-comment outcome.")
        };
    }

    private static async Task<IResult> ListCommentsAsync(
        Guid id,
        [AsParameters] ListTicketCommentsQueryRequest request,
        ListTicketCommentsUseCase useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!RequestActorContext.TryGet(httpContext, out var actor))
        {
            return Results.Forbid();
        }

        ListTicketCommentsResult? result;
        try
        {
            result = await useCase.ExecuteAsync(
                id,
                new ListTicketCommentsRequest(
                    request.Page ?? ListTicketCommentsRequest.DefaultPage,
                    request.PageSize ?? ListTicketCommentsRequest.DefaultPageSize),
                actor,
                cancellationToken);
        }
        catch (TicketCommentsValidationException exception)
        {
            return InvalidRequest(exception);
        }

        if (result is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new ListTicketCommentsResponse(
            result.Items.Select(ToCommentResponse).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount,
            result.TotalPages));
    }

    private static TicketCommentResponse ToCommentResponse(TicketCommentResult comment) =>
        new(
            comment.Id,
            comment.TicketId,
            comment.AuthorUserId,
            comment.Content,
            comment.CreatedAt);

    private static async Task<IResult> ListHistoryAsync(
        Guid id,
        [AsParameters] ListTicketHistoryQueryRequest request,
        ListTicketHistoryUseCase useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!RequestActorContext.TryGet(httpContext, out var actor))
        {
            return Results.Forbid();
        }

        ListTicketHistoryResult? result;
        try
        {
            result = await useCase.ExecuteAsync(
                id,
                new ListTicketHistoryRequest(
                    request.Page ?? ListTicketHistoryRequest.DefaultPage,
                    request.PageSize ?? ListTicketHistoryRequest.DefaultPageSize),
                actor,
                cancellationToken);
        }
        catch (TicketHistoryValidationException exception)
        {
            return InvalidRequest(exception);
        }

        if (result is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new ListTicketHistoryResponse(
            result.Items.Select(item => new TicketHistoryResponse(
                item.Id,
                item.TicketId,
                item.PerformedByUserId,
                item.EventType,
                item.OldValue,
                item.NewValue,
                item.CreatedAt)).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount,
            result.TotalPages));
    }

    private static IResult InvalidRequest(ArgumentException exception)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid request",
            detail: exception.Message);
    }

    private static bool IsCreateTicketValidationFailure(ArgumentException exception)
    {
        return exception.ParamName is
            "title" or
            "description" or
            "category" or
            "priority" or
            "createdByUserId";
    }

    private static bool IsListTicketsValidationFailure(ArgumentException exception)
    {
        return exception.ParamName is
            "status" or
            "priority" or
            "category" or
            "page" or
            "pageSize" or
            "createdFrom" or
            "sortField" or
            "sortDirection";
    }

}
