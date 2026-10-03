using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Tests.Application.Authentication;

public class RequestActorTests
{
    [Theory]
    [InlineData(UserRole.Employee)]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Administrator)]
    public void Constructor_ValidIdentity_CreatesActor(UserRole role)
    {
        var userId = Guid.NewGuid();

        var actor = new RequestActor(userId, role);

        Assert.Equal(userId, actor.UserId);
        Assert.Equal(role, actor.Role);
    }

    [Fact]
    public void Constructor_EmptyId_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new RequestActor(Guid.Empty, UserRole.Employee));

        Assert.Equal("userId", exception.ParamName);
    }

    [Fact]
    public void Constructor_UndefinedRole_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RequestActor(Guid.NewGuid(), (UserRole)999));

        Assert.Equal("role", exception.ParamName);
    }
}
