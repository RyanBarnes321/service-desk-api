using ServiceDesk.Core.Security;
using ServiceDesk.Infrastructure.Security;

namespace ServiceDesk.Api.Tests.Security;

public class AspNetCorePasswordServiceTests
{
    [Fact]
    public void HashAndVerify_Password_UsesOneWayCompatibleHasher()
    {
        var service = new AspNetCorePasswordService();

        var hash = service.Hash("correct-password");

        Assert.NotEqual("correct-password", hash);
        Assert.Equal(PasswordVerificationOutcome.Success, service.Verify(hash, "correct-password"));
        Assert.Equal(PasswordVerificationOutcome.Failed, service.Verify(hash, "wrong-password"));
    }

    [Fact]
    public void Verify_MalformedStoredHash_ReturnsFailed()
    {
        var service = new AspNetCorePasswordService();

        var result = service.Verify("not-valid-base64!", "any-password");

        Assert.Equal(PasswordVerificationOutcome.Failed, result);
    }
}
