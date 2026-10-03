using Microsoft.AspNetCore.Identity;
using System.Security.Cryptography;
using ServiceDesk.Core.Security;

namespace ServiceDesk.Infrastructure.Security;

public sealed class AspNetCorePasswordService : IPasswordService
{
    private static readonly object PasswordOwner = new();
    private readonly PasswordHasher<object> _passwordHasher = new();

    public AspNetCorePasswordService()
    {
        var dummyPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        DummyHash = _passwordHasher.HashPassword(PasswordOwner, dummyPassword);
    }

    public string DummyHash { get; }

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return _passwordHasher.HashPassword(PasswordOwner, password);
    }

    public PasswordVerificationOutcome Verify(string passwordHash, string password)
    {
        PasswordVerificationResult result;

        try
        {
            result = _passwordHasher.VerifyHashedPassword(PasswordOwner, passwordHash, password);
        }
        catch (FormatException)
        {
            return PasswordVerificationOutcome.Failed;
        }

        return result switch
        {
            PasswordVerificationResult.Success => PasswordVerificationOutcome.Success,
            PasswordVerificationResult.SuccessRehashNeeded =>
                PasswordVerificationOutcome.SuccessRehashNeeded,
            _ => PasswordVerificationOutcome.Failed
        };
    }
}
