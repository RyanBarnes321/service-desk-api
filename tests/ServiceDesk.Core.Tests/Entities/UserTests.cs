using ServiceDesk.Core.Entities;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Tests.Entities;

public class UserTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public void Create_WithValidValues_CreatesActiveUser()
    {
        var user = User.Create(
            "employee@example.com",
            "hashed-password",
            "Jane",
            "Doe",
            UserRole.Employee,
            CreatedAt);

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("employee@example.com", user.Email);
        Assert.Equal("hashed-password", user.PasswordHash);
        Assert.Equal("Jane", user.FirstName);
        Assert.Equal("Doe", user.LastName);
        Assert.Equal(UserRole.Employee, user.Role);
        Assert.Equal(CreatedAt, user.CreatedAt);
        Assert.True(user.IsActive);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithInvalidEmail_ThrowsArgumentException(string? email)
    {
        Assert.ThrowsAny<ArgumentException>(() => User.Create(
            email!,
            "hashed-password",
            "Jane",
            "Doe",
            UserRole.Employee,
            CreatedAt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithInvalidPasswordHash_ThrowsArgumentException(string? passwordHash)
    {
        Assert.ThrowsAny<ArgumentException>(() => User.Create(
            "employee@example.com",
            passwordHash!,
            "Jane",
            "Doe",
            UserRole.Employee,
            CreatedAt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithInvalidFirstName_ThrowsArgumentException(string? firstName)
    {
        Assert.ThrowsAny<ArgumentException>(() => User.Create(
            "employee@example.com",
            "hashed-password",
            firstName!,
            "Doe",
            UserRole.Employee,
            CreatedAt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithInvalidLastName_ThrowsArgumentException(string? lastName)
    {
        Assert.ThrowsAny<ArgumentException>(() => User.Create(
            "employee@example.com",
            "hashed-password",
            "Jane",
            lastName!,
            UserRole.Employee,
            CreatedAt));
    }

    [Fact]
    public void Create_WithUndefinedRole_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => User.Create(
            "employee@example.com",
            "hashed-password",
            "Jane",
            "Doe",
            (UserRole)999,
            CreatedAt));
    }

    [Fact]
    public void ChangeRole_WithDefinedRole_ChangesRole()
    {
        var user = CreateUser();

        user.ChangeRole(UserRole.Administrator);

        Assert.Equal(UserRole.Administrator, user.Role);
    }

    [Fact]
    public void ChangeRole_WithUndefinedRole_ThrowsArgumentOutOfRangeException()
    {
        var user = CreateUser();

        Assert.Throws<ArgumentOutOfRangeException>(() => user.ChangeRole((UserRole)999));
    }

    [Fact]
    public void Deactivate_WhenCalledTwice_RemainsInactive()
    {
        var user = CreateUser();

        user.Deactivate();
        user.Deactivate();

        Assert.False(user.IsActive);
    }

    [Fact]
    public void Activate_WhenInactive_BecomesActive()
    {
        var user = CreateUser();
        user.Deactivate();

        user.Activate();

        Assert.True(user.IsActive);
    }

    [Fact]
    public void Activate_WhenAlreadyActive_RemainsActive()
    {
        var user = CreateUser();

        user.Activate();

        Assert.True(user.IsActive);
    }

    private static User CreateUser()
    {
        return User.Create(
            "employee@example.com",
            "hashed-password",
            "Jane",
            "Doe",
            UserRole.Employee,
            CreatedAt);
    }
}
