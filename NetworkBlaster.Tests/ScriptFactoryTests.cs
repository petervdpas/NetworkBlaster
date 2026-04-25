using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NetworkBlaster;
using NetworkBlaster.Tests.Support;
using Xunit;

namespace NetworkBlaster.Tests;

public class ScriptFactoryTests
{
    [Fact]
    public async Task WithToken_SendsBearerHeader()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK);
        var client = NetClient.WithToken("https://example.test/", "ghp_xxx", new HttpClient(handler));

        await client.GetAsync("ping");

        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization?.Scheme);
        Assert.Equal("ghp_xxx", handler.LastRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task Anonymous_SendsNoAuthHeader()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK);
        var client = NetClient.Anonymous("https://example.test/", new HttpClient(handler));

        await client.GetAsync("ping");

        Assert.Null(handler.LastRequest.Headers.Authorization);
    }

    [Fact]
    public async Task WithApiKey_AttachesCustomHeader_OnEveryRequest()
    {
        var handler = new RecordingHandler()
            .RespondWith(HttpStatusCode.OK)
            .RespondWith(HttpStatusCode.OK);
        var client = NetClient.WithApiKey("https://example.test/", "X-API-Key", "secret", new HttpClient(handler));

        await client.GetAsync("first");
        await client.GetAsync("second");

        Assert.Equal("secret", string.Join(",", handler.Requests[0].Headers.GetValues("X-API-Key")));
        Assert.Equal("secret", string.Join(",", handler.Requests[1].Headers.GetValues("X-API-Key")));
        Assert.Null(handler.Requests[0].Headers.Authorization);
    }

    [Fact]
    public async Task WithBasicAuth_EncodesCredentials_AsBase64BasicScheme()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK);
        var client = NetClient.WithBasicAuth("https://example.test/", "alice", "wonderland", new HttpClient(handler));

        await client.GetAsync("ping");

        var auth = handler.LastRequest.Headers.Authorization;
        Assert.Equal("Basic", auth?.Scheme);

        var decoded = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(auth!.Parameter!));
        Assert.Equal("alice:wonderland", decoded);
    }

    [Fact]
    public async Task WithDefaultHeader_ChainsOnFactoriesAndPersistsAcrossRequests()
    {
        var handler = new RecordingHandler()
            .RespondWith(HttpStatusCode.OK)
            .RespondWith(HttpStatusCode.OK);
        var client = NetClient
            .Anonymous("https://example.test/", new HttpClient(handler))
            .WithDefaultHeader("User-Agent", "my-script/1.0");

        await client.GetAsync("a");
        await client.GetAsync("b");

        Assert.Equal("my-script/1.0", string.Join(",", handler.Requests[0].Headers.GetValues("User-Agent")));
        Assert.Equal("my-script/1.0", string.Join(",", handler.Requests[1].Headers.GetValues("User-Agent")));
    }
}
