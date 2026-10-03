using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.Authentication;

public sealed record CurrentUserState(Guid UserId, UserRole Role, bool IsActive);
