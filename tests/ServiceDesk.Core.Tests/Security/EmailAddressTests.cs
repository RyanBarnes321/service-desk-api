using ServiceDesk.Core.Security;

namespace ServiceDesk.Core.Tests.Security;

public class EmailAddressTests
{
    [Fact]
    public void Normalize_MixedCaseAndWhitespace_TrimsAndLowersInvariant()
    {
        Assert.Equal("admin@example.com", EmailAddress.Normalize("  Admin@Example.COM  "));
    }
}
