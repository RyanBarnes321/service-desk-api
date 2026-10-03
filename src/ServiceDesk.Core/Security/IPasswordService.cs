namespace ServiceDesk.Core.Security;

public interface IPasswordService
{
    string DummyHash { get; }

    string Hash(string password);

    PasswordVerificationOutcome Verify(string passwordHash, string password);
}
