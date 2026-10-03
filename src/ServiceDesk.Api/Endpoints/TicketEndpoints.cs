using ServiceDesk.Api.Contracts.Tickets;
using ServiceDesk.Core.Application.Tickets.CreateTicket;
using ServiceDesk.Core.Application.Tickets.GetTicket;
using ServiceDesk.Core.Application.Tickets.ListTickets;

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

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateTicketRequest request,
        CreateTicketUseCase useCase,
        CancellationToken cancellationToken)
    {
        try
        {
            // TODO: Derive CreatedByUserId from authenticated identity once authentication is introduced.
            var command = new CreateTicketCommand(
                request.Title,
                request.Description,
                request.Category,
                request.Priority,
                request.CreatedByUserId);
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
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await useCase.ExecuteAsync(id, cancellationToken);

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
        CancellationToken cancellationToken)
    {
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
            var result = await useCase.ExecuteAsync(applicationRequest, cancellationToken);
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
