using System;
using System.Net;
using NetworkBlast;
using Xunit;

namespace NetworkBlast.Tests.Auth;

// Note: NTLM/Negotiate handshake itself can't be unit-tested without a real
// challenger, so these only verify factory wiring and validation.
public class WindowsAuthTests
{
    [Fact]
    public void WithWindowsAuth_DefaultCredentials_BuildsClientWithoutThrowing()
    {
        var client = NetClient.WithWindowsAuth("https://intranet.example.test/");
        Assert.NotNull(client);
        Assert.Equal("inline", client.ConnectionName);
    }

    [Fact]
    public void WithWindowsAuth_ExplicitCredentials_BuildsClientWithoutThrowing()
    {
        var creds = new NetworkCredential("svc-account", "p@ssw0rd", "CORP");
        var client = NetClient.WithWindowsAuth("https://intranet.example.test/", creds);
        Assert.NotNull(client);
    }

    [Fact]
    public void WithWindowsAuth_NullBaseUrl_Throws()
        => Assert.Throws<ArgumentException>(() => NetClient.WithWindowsAuth(""));

    [Fact]
    public void WithWindowsAuth_NullCredentials_Throws()
        => Assert.Throws<ArgumentNullException>(() => NetClient.WithWindowsAuth("https://x.test/", credentials: null!));
}
