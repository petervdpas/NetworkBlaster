using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NetworkBlaster;
using NetworkBlaster.Tests.Support;
using Xunit;

namespace NetworkBlaster.Tests;

public class NetClientTests
{
    [Fact]
    public async Task GetAsync_HydratesBaseUrlAndBearerTokenFromResolver_OnFirstRequest()
    {
        var calls = new List<(string category, string key)>();
        Task<string> Resolver(string category, string key, CancellationToken ct)
        {
            calls.Add((category, key));
            return key switch
            {
                "baseUrl" => Task.FromResult("https://example.test/"),
                "token"   => Task.FromResult("ghp_test"),
                _         => Task.FromResult(string.Empty),
            };
        }

        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK);
        var http = new HttpClient(handler);
        var client = new NetClient(Resolver, "github", http);

        var response = await client.GetAsync("repos/octocat/hello-world");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Uri("https://example.test/repos/octocat/hello-world"), handler.LastRequest.RequestUri);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization?.Scheme);
        Assert.Equal("ghp_test", handler.LastRequest.Headers.Authorization?.Parameter);
        Assert.Contains(("github", "baseUrl"), calls);
        Assert.Contains(("github", "token"), calls);
    }

    [Fact]
    public async Task EnsureHydratedAsync_FiresResolverOnce_UnderConcurrentFirstRequests()
    {
        var baseUrlCalls = 0;
        Task<string> Resolver(string category, string key, CancellationToken ct)
        {
            if (key == "baseUrl") Interlocked.Increment(ref baseUrlCalls);
            return Task.FromResult(key == "baseUrl" ? "https://example.test/" : string.Empty);
        }

        var handler = new RecordingHandler();
        for (var i = 0; i < 8; i++) handler.RespondWith(HttpStatusCode.OK);

        var client = new NetClient(Resolver, "shared", new HttpClient(handler));

        var tasks = new Task[8];
        for (var i = 0; i < tasks.Length; i++) tasks[i] = client.GetAsync($"path/{i}");
        await Task.WhenAll(tasks);

        Assert.Equal(1, baseUrlCalls);
    }

    [Fact]
    public async Task SendAsync_ThrowsClearError_WhenBaseUrlMissing()
    {
        Task<string> Resolver(string category, string key, CancellationToken ct) => Task.FromResult(string.Empty);
        var client = new NetClient(Resolver, "broken", new HttpClient(new RecordingHandler()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("/anything"));
        Assert.Contains("baseUrl", ex.Message);
        Assert.Contains("broken", ex.Message);
    }

    [Fact]
    public async Task GetAsync_AppendsRelativePathToBaseUrl_WithoutLeadingSlash()
    {
        Task<string> Resolver(string c, string k, CancellationToken ct)
            => Task.FromResult(k == "baseUrl" ? "https://example.test/api/" : string.Empty);

        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK);
        var client = new NetClient(Resolver, "x", new HttpClient(handler));

        await client.GetAsync("widgets/42");

        Assert.Equal(new Uri("https://example.test/api/widgets/42"), handler.LastRequest.RequestUri);
    }
}
