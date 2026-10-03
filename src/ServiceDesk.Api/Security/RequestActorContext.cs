using ServiceDesk.Core.Application.Authentication;

namespace ServiceDesk.Api.Security;

public static class RequestActorContext
{
    private static readonly object ActorKey = new();

    public static void Set(HttpContext httpContext, RequestActor actor)
    {
        httpContext.Items[ActorKey] = actor;
    }

    public static bool TryGet(HttpContext httpContext, out RequestActor actor)
    {
        if (httpContext.Items.TryGetValue(ActorKey, out var value) && value is RequestActor requestActor)
        {
            actor = requestActor;
            return true;
        }

        actor = null!;
        return false;
    }
}
