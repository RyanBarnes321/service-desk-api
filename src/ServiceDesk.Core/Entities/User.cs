using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Entities;

public class User
{
    private User(
        Guid id,
        string email,
        string passwordHash,
        string firstName,
        string lastName,
        UserRole role,
        DateTimeOffset createdAt)
    {
        Id = id;
        Email = email;
        PasswordHash = passwordHash;
        FirstName = firstName;
        LastName = lastName;
        Role = role;
        CreatedAt = createdAt;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; }

    public string PasswordHash { get; private set; }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    public UserRole Role { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsActive { get; private set; }

    public static User Create(
        string email,
        string passwordHash,
        string firstName,
        string lastName,
        UserRole role,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
        EnsureRoleIsDefined(role);

        return new User(
            Guid.NewGuid(),
            email,
            passwordHash,
            firstName,
            lastName,
            role,
            createdAt);
    }

    public void ChangeRole(UserRole role)
    {
        EnsureRoleIsDefined(role);
        Role = role;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void Activate()
    {
        IsActive = true;
    }

    private static void EnsureRoleIsDefined(UserRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Role must be a defined value.");
        }
    }
}
