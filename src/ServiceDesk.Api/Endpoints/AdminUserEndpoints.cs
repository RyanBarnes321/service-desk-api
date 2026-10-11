using ServiceDesk.Api.Contracts.Users;
using ServiceDesk.Api.Security;
using ServiceDesk.Core.Application.UserManagement;

namespace ServiceDesk.Api.Endpoints;

public static class AdminUserEndpoints
{
    public static IEndpointRouteBuilder MapAdminUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var users = endpoints.MapGroup("/api/users");
        users.MapGet("/", ListAsync)
            .WithSummary("List users")
            .WithDescription("Administrator-only deterministic user pagination. Password hashes are never returned.")
            .Produces<ListAdminUsersResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
        users.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Get a user")
            .WithDescription("Administrator-only safe account projection by ID.")
            .Produces<AdminUserResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
        users.MapPost("/", CreateAsync)
            .WithSummary("Create a managed user")
            .WithDescription(
                "Administrator-only creation of an Employee or Technician. Email is normalized and unique. " +
                "Example role value: \"Technician\".")
            .Produces<AdminUserResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
        users.MapPut("/{id:guid}/role", ChangeRoleAsync)
            .WithSummary("Change a user's role")
            .WithDescription(
                "Administrator-only role change. Self-demotion, removal of the final active Administrator, " +
                "and serializable write conflicts return 409.")
            .Produces<AdminUserResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        users.MapPut("/{id:guid}/activation", SetActivationAsync)
            .WithSummary("Activate or deactivate a user")
            .WithDescription(
                "Administrator-only activation control. Self-deactivation, removal of the final active " +
                "Administrator, and serializable write conflicts return 409.")
            .Produces<AdminUserResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        users.WithTags("Users");
        users.RequireAuthorization(ActiveUserPolicy.Name);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        [AsParameters] ListAdminUsersQueryRequest request,
        ListAdminUsersUseCase useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!RequestActorContext.TryGet(httpContext, out var actor)) return Results.Forbid();

        ListAdminUsersExecution execution;
        try
        {
            execution = await useCase.ExecuteAsync(
                new ListAdminUsersRequest(
                    request.Page ?? ListAdminUsersRequest.DefaultPage,
                    request.PageSize ?? ListAdminUsersRequest.DefaultPageSize),
                actor,
                cancellationToken);
        }
        catch (AdminUserValidationException exception)
        {
            return InvalidRequest(exception);
        }

        return execution.Outcome switch
        {
            AdminUserOutcome.Success => Results.Ok(new ListAdminUsersResponse(
                execution.Result!.Items.Select(ToResponse).ToArray(),
                execution.Result.Page,
                execution.Result.PageSize,
                execution.Result.TotalCount,
                execution.Result.TotalPages)),
            AdminUserOutcome.Forbidden => Results.Forbid(),
            _ => throw new InvalidOperationException("Unknown list-users outcome.")
        };
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        GetAdminUserUseCase useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!RequestActorContext.TryGet(httpContext, out var actor)) return Results.Forbid();
        AdminUserExecution execution;
        try
        {
            execution = await useCase.ExecuteAsync(id, actor, cancellationToken);
        }
        catch (AdminUserValidationException exception)
        {
            return InvalidRequest(exception);
        }

        return ToResult(execution);
    }

    private static async Task<IResult> CreateAsync(
        CreateAdminUserHttpRequest request,
        CreateAdminUserUseCase useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!RequestActorContext.TryGet(httpContext, out var actor)) return Results.Forbid();
        AdminUserExecution execution;
        try
        {
            execution = await useCase.ExecuteAsync(
                new CreateAdminUserRequest(
                    request.Email,
                    request.Password,
                    request.FirstName,
                    request.LastName,
                    request.Role ?? (ServiceDesk.Core.Enums.UserRole)(-1)),
                actor,
                cancellationToken);
        }
        catch (AdminUserValidationException exception)
        {
            return InvalidRequest(exception);
        }

        return execution.Outcome switch
        {
            AdminUserOutcome.Success => Results.Created(
                $"/api/users/{execution.User!.Id}",
                ToResponse(execution.User)),
            AdminUserOutcome.Forbidden => Results.Forbid(),
            AdminUserOutcome.DuplicateEmail => Conflict(
                "User email conflict",
                "A user with that email already exists."),
            _ => throw new InvalidOperationException("Unknown create-user outcome.")
        };
    }

    private static Task<IResult> ChangeRoleAsync(
        Guid id,
        ChangeAdminUserRoleRequest request,
        MutateAdminUserUseCase useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        return ExecuteMutationAsync(
            httpContext,
            actor => useCase.ChangeRoleAsync(
                id,
                request.Role ?? (ServiceDesk.Core.Enums.UserRole)(-1),
                actor,
                cancellationToken));
    }

    private static Task<IResult> SetActivationAsync(
        Guid id,
        SetAdminUserActivationRequest request,
        MutateAdminUserUseCase useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        return ExecuteMutationAsync(
            httpContext,
            actor => useCase.SetActivationAsync(id, request.IsActive, actor, cancellationToken));
    }

    private static async Task<IResult> ExecuteMutationAsync(
        HttpContext httpContext,
        Func<ServiceDesk.Core.Application.Authentication.RequestActor,
            Task<AdminUserExecution>> execute)
    {
        if (!RequestActorContext.TryGet(httpContext, out var actor)) return Results.Forbid();
        AdminUserExecution execution;
        try
        {
            execution = await execute(actor);
        }
        catch (AdminUserValidationException exception)
        {
            return InvalidRequest(exception);
        }

        return ToResult(execution);
    }

    private static IResult ToResult(AdminUserExecution execution) => execution.Outcome switch
    {
        AdminUserOutcome.Success => Results.Ok(ToResponse(execution.User!)),
        AdminUserOutcome.Forbidden => Results.Forbid(),
        AdminUserOutcome.NotFound => Results.NotFound(),
        AdminUserOutcome.SelfLockout => Conflict(
            "Administrator self-lockout prevented",
            "Administrators cannot demote or deactivate themselves."),
        AdminUserOutcome.LastActiveAdministrator => Conflict(
            "Final administrator removal prevented",
            "The final active Administrator cannot be demoted or deactivated."),
        AdminUserOutcome.Conflict => Conflict(
            "User update conflict",
            "The user set changed concurrently. Refresh and try again."),
        _ => throw new InvalidOperationException("Unknown user-management outcome.")
    };

    private static AdminUserResponse ToResponse(AdminUserResult user) =>
        new(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.Role,
            user.IsActive,
            user.CreatedAt);

    private static IResult InvalidRequest(ArgumentException exception) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid request",
            detail: exception.Message);

    private static IResult Conflict(string title, string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: title,
            detail: detail);
}
