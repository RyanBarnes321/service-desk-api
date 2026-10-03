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

        if (context.Resource is not HttpContext httpContext)
        {
            context.Fail();
            return;
        }

        var state = await userStateQuery.FindAsync(userId, httpContext.RequestAborted);

        if (state is not null &&
            state.UserId == userId &&
            state.IsActive &&
            Enum.IsDefined(state.Role))
        {
            RequestActorContext.Set(httpContext, new RequestActor(state.UserId, state.Role));
            context.Succeed(requirement);
        }
        else
        {
            context.Fail();
        }
    }
}
