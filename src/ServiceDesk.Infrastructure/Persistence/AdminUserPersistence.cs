using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ServiceDesk.Core.Application.UserManagement;
using ServiceDesk.Core.Entities;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Infrastructure.Persistence;

public sealed class AdminUserPersistence(ServiceDeskDbContext dbContext)
    : IAdminUserQuery, ICreateAdminUserPersistence, IAdminUserMutationPersistence
{
    public async Task<ListAdminUsersResult> ListAsync(
        ListAdminUsersRequest request,
        CancellationToken cancellationToken)
    {
        var users = dbContext.Users.AsNoTracking();
        var totalCount = await users.CountAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var offset = (long)(request.Page - 1) * request.PageSize;
        IReadOnlyList<AdminUserResult> items = offset > int.MaxValue
            ? []
            : await users
                .OrderBy(user => user.CreatedAt)
                .ThenBy(user => user.Id)
                .Skip((int)offset)
                .Take(request.PageSize)
                .Select(user => new AdminUserResult(
                    user.Id,
                    user.Email,
                    user.FirstName,
                    user.LastName,
                    user.Role,
                    user.IsActive,
                    user.CreatedAt))
                .ToArrayAsync(cancellationToken);
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)request.PageSize);

        return new(items, request.Page, request.PageSize, totalCount, totalPages);
    }

    public Task<AdminUserResult?> FindAsync(Guid userId, CancellationToken cancellationToken)
    {
        return dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new AdminUserResult(
                user.Id,
                user.Email,
                user.FirstName,
                user.LastName,
                user.Role,
                user.IsActive,
                user.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<AdminUserOutcome> CreateAsync(
        User user,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Users.AnyAsync(
                candidate => candidate.Email == user.Email,
                cancellationToken))
        {
            return AdminUserOutcome.DuplicateEmail;
        }

        dbContext.Users.Add(user);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return AdminUserOutcome.Success;
        }
        catch (DbUpdateException exception) when (IsUniqueEmailViolation(exception))
        {
            return AdminUserOutcome.DuplicateEmail;
        }
    }

    public async Task<AdminUserExecution> ExecuteAsync(
        AdminUserMutationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var actor = await dbContext.Users.SingleOrDefaultAsync(
                user => user.Id == request.ActingAdministratorId,
                cancellationToken);
            if (actor is null || !actor.IsActive || actor.Role != UserRole.Administrator)
            {
                return new(AdminUserOutcome.Forbidden);
            }

            var target = request.TargetUserId == actor.Id
                ? actor
                : await dbContext.Users.SingleOrDefaultAsync(
                    user => user.Id == request.TargetUserId,
                    cancellationToken);
            if (target is null)
            {
                return new(AdminUserOutcome.NotFound);
            }

            if (actor.Id == target.Id &&
                (request.Mutation == AdminUserMutation.Deactivate ||
                 request.Mutation == AdminUserMutation.ChangeRole &&
                 request.Role != UserRole.Administrator))
            {
                return new(AdminUserOutcome.SelfLockout);
            }

            var removesActiveAdministrator =
                target.IsActive &&
                target.Role == UserRole.Administrator &&
                (request.Mutation == AdminUserMutation.Deactivate ||
                 request.Mutation == AdminUserMutation.ChangeRole &&
                 request.Role != UserRole.Administrator);
            if (removesActiveAdministrator)
            {
                var activeAdministratorCount = await dbContext.Users.CountAsync(
                    user => user.IsActive && user.Role == UserRole.Administrator,
                    cancellationToken);
                if (activeAdministratorCount <= 1)
                {
                    return new(AdminUserOutcome.LastActiveAdministrator);
                }
            }

            Apply(target, request);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new(AdminUserOutcome.Success, AdminUserResult.From(target));
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            return new(AdminUserOutcome.Conflict);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.SerializationFailure
            })
        {
            return new(AdminUserOutcome.Conflict);
        }
    }

    private static void Apply(User target, AdminUserMutationRequest request)
    {
        switch (request.Mutation)
        {
            case AdminUserMutation.ChangeRole:
                target.ChangeRole(request.Role!.Value);
                break;
            case AdminUserMutation.Activate:
                target.Activate();
                break;
            case AdminUserMutation.Deactivate:
                target.Deactivate();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request));
        }
    }

    private static bool IsUniqueEmailViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ix_users_email"
        };
    }
}
