using ServiceDesk.Api.Contracts.Authentication;
using ServiceDesk.Core.Application.Authentication.Login;

namespace ServiceDesk.Api.Endpoints;

public static class AuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/login", LoginAsync).AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginHttpRequest request,
        LoginUseCase useCase,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await useCase.ExecuteAsync(
                new LoginRequest(request.Email, request.Password),
                cancellationToken);

            if (!result.IsSuccess)
            {
                return InvalidCredentials();
            }

            return Results.Ok(new LoginResponse(result.AccessToken!, result.ExpiresAt!.Value));
        }
        catch (ArgumentException exception) when (exception.ParamName is "email" or "password")
        {
            return InvalidCredentials();
        }
    }

    private static IResult InvalidCredentials()
    {
        return Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Invalid credentials");
    }
}
