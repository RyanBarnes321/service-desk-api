using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Application.Tickets;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Tests.Application.Tickets;

public class TicketVisibilityScopeTests
{
    [Fact]
    public void For_Employee_RestrictsVisibilityToActor()
    {
        var actor = new RequestActor(Guid.NewGuid(), UserRole.Employee);

        var scope = TicketVisibilityScope.For(actor);

        Assert.Equal(actor.UserId, scope.CreatedByUserId);
    }

    [Theory]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Administrator)]
    public void For_PrivilegedActor_ProvidesBroadVisibility(UserRole role)
    {
        var actor = new RequestActor(Guid.NewGuid(), role);

        var scope = TicketVisibilityScope.For(actor);

        Assert.Null(scope.CreatedByUserId);
    }
}
