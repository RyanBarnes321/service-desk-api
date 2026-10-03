namespace ServiceDesk.Core.Application.Authentication.Login;

public interface ILoginUserQuery
{
    Task<LoginUser?> FindByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken);
}
