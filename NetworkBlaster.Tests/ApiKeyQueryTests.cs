using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NetworkBlaster;
using NetworkBlaster.Tests.Support;
using Xunit;

namespace NetworkBlaster.Tests;

public class ApiKeyQueryTests
{
    [Fact]
    public async Task WithApiKeyQuery_AppendsParameterToEveryRequest()
    {
        var handler = new RecordingHandler()
            .RespondWith(HttpStatusCode.OK)
            .RespondWith(HttpStatusCode.OK);
        var client = NetClient.WithApiKeyQuery("https://example.test/", "appid", "abc123", new HttpClient(handler));

        await client.GetAsync("first");
        await client.GetAsync("second");

        Assert.Contains("appid=abc123", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("appid=abc123", handler.Requests[1].RequestUri!.Query);
    }

    [Fact]
    public async Task WithApiKeyQuery_MergesWithPerRequestQuery()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK);
        var client = NetClient.WithApiKeyQuery("https://example.test/", "appid", "abc123", new HttpClient(handler));

        await client.GetAsync("data", Options.Query(("city", "Paris"), ("units", "metric")));

        var query = handler.LastRequest.RequestUri!.Query;
        Assert.Contains("appid=abc123", query);
        Assert.Contains("city=Paris", query);
        Assert.Contains("units=metric", query);
    }

    [Fact]
    public async Task PerRequestQuery_Wins_OnKeyCollisionWithDefault()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK);
        var client = NetClient.WithApiKeyQuery("https://example.test/", "appid", "default-key", new HttpClient(handler));

        await client.GetAsync("data", Options.Query(("appid", "override-key")));

        var query = handler.LastRequest.RequestUri!.Query;
        Assert.Contains("appid=override-key", query);
        Assert.DoesNotContain("appid=default-key", query);
    }

    [Fact]
    public async Task WithDefaultQuery_ChainableMutator_AppendsToEveryRequest()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK);
        var client = NetClient.Anonymous("https://example.test/", new HttpClient(handler))
            .WithDefaultQuery("api-version", "2024-01-01")
            .WithDefaultQuery("tenant", "acme");

        await client.GetAsync("data");

        var query = handler.LastRequest.RequestUri!.Query;
        Assert.Contains("api-version=2024-01-01", query);
        Assert.Contains("tenant=acme", query);
    }

    [Fact]
    public async Task DefaultQuery_PreservesExistingQueryStringInPath()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK);
        var client = NetClient.WithApiKeyQuery("https://example.test/", "appid", "abc", new HttpClient(handler));

        await client.GetAsync("search?builtin=1");

        var query = handler.LastRequest.RequestUri!.Query;
        Assert.Contains("builtin=1", query);
        Assert.Contains("appid=abc", query);
    }
}
