using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NetworkBlaster;
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

        var handler = new RecordingHandler(HttpStatusCode.OK);
        var http = new HttpClient(handler);
        var client = new NetClient(Resolver, "github", http);

        var response = await client.GetAsync("repos/octocat/hello-world");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(new Uri("https://example.test/repos/octocat/hello-world"), handler.LastRequest!.RequestUri);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization?.Scheme);
        Assert.Equal("ghp_test", handler.LastRequest.Headers.Authorization?.Parameter);
        Assert.Contains(("github", "baseUrl"), calls);
        Assert.Contains(("github", "token"), calls);
    }

    [Fact]
    public async Task WithToken_ProducesClientWithBearerAuth_NoResolverNeeded()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var client = NetClient.WithToken("https://example.test/", "ghp_xxx", new HttpClient(handler));

        var response = await client.GetAsync("ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Uri("https://example.test/ping"), handler.LastRequest!.RequestUri);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization?.Scheme);
        Assert.Equal("ghp_xxx", handler.LastRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task Anonymous_ProducesClientWithoutAuthHeader()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var client = NetClient.Anonymous("https://example.test/", new HttpClient(handler));

        await client.GetAsync("ping");

        Assert.Null(handler.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task SendAsync_ThrowsClearError_WhenBaseUrlMissing()
    {
        Task<string> Resolver(string category, string key, CancellationToken ct) => Task.FromResult(string.Empty);

        var client = new NetClient(Resolver, "broken", new HttpClient(new RecordingHandler(HttpStatusCode.OK)));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("/anything"));
        Assert.Contains("baseUrl", ex.Message);
        Assert.Contains("broken", ex.Message);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        public HttpRequestMessage? LastRequest { get; private set; }

        public RecordingHandler(HttpStatusCode status) => _status = status;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(_status));
        }
    }
}
