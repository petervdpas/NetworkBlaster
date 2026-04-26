using System;
using System.Net;
using System.Reflection;
using System.Net.Http;
using NetworkBlast;
using Xunit;

namespace NetworkBlast.Tests.Auth;

// Cookie behavior is owned by HttpClientHandler; we can't intercept Set-Cookie
// without replacing that handler. These tests verify factory wiring + container reuse.
public class CookieJarTests
{
    [Fact]
    public void WithCookieJar_NoArgs_BuildsClientWithFreshContainer()
    {
        var client = NetClient.WithCookieJar("https://example.test/");
        Assert.NotNull(client);
    }

    [Fact]
    public void WithCookieJar_WithCustomContainer_PreservesReference()
    {
        var jar = new CookieContainer();
        jar.Add(new Uri("https://example.test/"), new Cookie("session", "abc"));

        var client = NetClient.WithCookieJar("https://example.test/", jar);

        var handler = ExtractHandler(client);
        Assert.Same(jar, handler.CookieContainer);
        Assert.Equal(1, handler.CookieContainer.Count);
    }

    [Fact]
    public void WithCookieJar_ThrowsOnEmptyBaseUrl()
        => Assert.Throws<ArgumentException>(() => NetClient.WithCookieJar(""));

    private static HttpClientHandler ExtractHandler(NetClient client)
    {
        // The NetClient holds a private HttpClient; reach in to verify the cookie container.
        var httpField = typeof(NetClient).GetField("_http", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var http = (HttpClient)httpField.GetValue(client)!;

        var handlerField = typeof(HttpMessageInvoker).GetField("_handler", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var handler = (HttpClientHandler)handlerField.GetValue(http)!;
        return handler;
    }
}
