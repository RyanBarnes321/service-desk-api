using ServiceDesk.Api.Contracts.Tickets;
using ServiceDesk.Api.Security;
using ServiceDesk.Core.Application.Tickets.Assignment;
using ServiceDesk.Core.Application.Tickets.CreateTicket;
using ServiceDesk.Core.Application.Tickets.GetTicket;
using ServiceDesk.Core.Application.Tickets.ListTickets;
using ServiceDesk.Core.Application.Tickets.TechnicalOperations;

namespace ServiceDesk.Api.Endpoints;

public static class TicketEndpoints
{
    private const string GetTicketRouteName = "GetTicket";

    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var tickets = endpoints.MapGroup("/api/tickets");

        tickets.MapPost("/", CreateAsync);
        tickets.MapGet("/", ListAsync);
        tickets.MapGet("/{id:guid}", GetAsync).WithName(GetTicketRouteName);
        tickets.MapPost("/{id:guid}/claim", ClaimAsync);
        tickets.MapPut("/{id:guid}/assignment", AssignAsync);
        tickets.MapDelete("/{id:guid}/assignment", UnassignAsync);
        tickets.MapPost("/{id:guid}/start", StartWorkAsync);
        tickets.MapPost("/{id:guid}/wait", WaitAsync);
        tickets.MapPost("/{id:guid}/resume", ResumeAsync);
        tickets.MapPost("/{id:guid}/resolve", ResolveAsync);
        tickets.MapPut("/{id:guid}/priority", ChangePriorityAsync);
        tickets.RequireAuthorization(ActiveUserPolicy.Name);

        return endpoints;
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
