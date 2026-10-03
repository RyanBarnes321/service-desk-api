using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.Authentication;

public sealed record RequestActor
{
    public RequestActor(Guid userId, UserRole role)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(userId));
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Role must be a defined value.");
        }

        UserId = userId;
        Role = role;
    }

    public Guid UserId { get; }

    public UserRole Role { get; }
}
