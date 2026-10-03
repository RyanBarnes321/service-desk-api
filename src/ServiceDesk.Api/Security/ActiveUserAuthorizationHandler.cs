using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using ServiceDesk.Core.Application.Authentication;

namespace ServiceDesk.Api.Security;

public sealed class ActiveUserAuthorizationHandler(ICurrentUserStateQuery userStateQuery)
    : AuthorizationHandler<ActiveUserRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveUserRequirement requirement)
    {
        var subject = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(subject, out var userId) || userId == Guid.Empty)
        {
            context.Fail();
            return;
        }

        var cancellationToken = context.Resource is HttpContext httpContext
            ? httpContext.RequestAborted
            : CancellationToken.None;

        if (await userStateQuery.IsActiveAsync(userId, cancellationToken))
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail();
        }
    }
}
