using System.Net.Mail;
using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Entities;
using ServiceDesk.Core.Enums;
using ServiceDesk.Core.Security;

namespace ServiceDesk.Core.Application.UserManagement;

public sealed class CreateAdminUserUseCase(
    ICreateAdminUserPersistence persistence,
    IPasswordService passwordService,
    TimeProvider timeProvider)
{
    public async Task<AdminUserExecution> ExecuteAsync(
        CreateAdminUserRequest request,
        RequestActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);
        cancellationToken.ThrowIfCancellationRequested();

        if (actor.Role != UserRole.Administrator)
        {
            return new(AdminUserOutcome.Forbidden);
        }

        Validate(request);
        var email = EmailAddress.Normalize(request.Email!);
        var user = User.Create(
            email,
            passwordService.Hash(request.Password!),
            request.FirstName!.Trim(),
            request.LastName!.Trim(),
            request.Role,
            timeProvider.GetUtcNow());
        var outcome = await persistence.CreateAsync(user, cancellationToken);

        return outcome == AdminUserOutcome.Success
            ? new(outcome, AdminUserResult.From(user))
            : new(outcome);
    }

    private static void Validate(CreateAdminUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            throw new AdminUserValidationException("Email is required.", "email");
        var email = EmailAddress.Normalize(request.Email);
        if (email.Length > 254 ||
            !MailAddress.TryCreate(email, out var parsed) ||
            !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase))
        {
            throw new AdminUserValidationException("Email is invalid.", "email");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
            throw new AdminUserValidationException("Password is required.", "password");
        ValidateName(request.FirstName, "firstName");
        ValidateName(request.LastName, "lastName");
        if (request.Role is not UserRole.Employee and not UserRole.Technician)
        {
            throw new AdminUserValidationException(
                "Only Employee and Technician accounts can be created.",
                "role");
        }
    }

    private static void ValidateName(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 100)
        {
            throw new AdminUserValidationException(
                $"{parameterName} is required and cannot exceed 100 characters.",
                parameterName);
        }
    }

}
