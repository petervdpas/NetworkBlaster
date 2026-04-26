using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NetworkBlast.Auth;
using NetworkBlast.Tests.Support;
using Xunit;

namespace NetworkBlast.Tests.Auth;

public class OAuth2TokenProviderTests
{
    private static readonly Uri TokenEndpoint = new("https://login.example.test/oauth2/token");

    [Fact]
    public async Task GetAccessTokenAsync_FetchesAndReturnsToken()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK,
            "{\"access_token\":\"abc\",\"token_type\":\"Bearer\",\"expires_in\":3600}");
        var provider = new OAuth2TokenProvider(TokenEndpoint, "client", "secret", scope: null, new HttpClient(handler));

        var token = await provider.GetAccessTokenAsync();

        Assert.Equal("abc", token);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetAccessTokenAsync_CachesToken_OnSubsequentCallsWithinExpiry()
    {
        var handler = new RecordingHandler()
            .RespondWith(HttpStatusCode.OK, "{\"access_token\":\"abc\",\"expires_in\":3600}");
        var provider = new OAuth2TokenProvider(TokenEndpoint, "client", "secret", scope: null, new HttpClient(handler));

        await provider.GetAccessTokenAsync();
        await provider.GetAccessTokenAsync();
        await provider.GetAccessTokenAsync();

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task InvalidateAsync_ForcesRefetch()
    {
        var handler = new RecordingHandler()
            .RespondWith(HttpStatusCode.OK, "{\"access_token\":\"first\",\"expires_in\":3600}")
            .RespondWith(HttpStatusCode.OK, "{\"access_token\":\"second\",\"expires_in\":3600}");
        var provider = new OAuth2TokenProvider(TokenEndpoint, "client", "secret", scope: null, new HttpClient(handler));

        var a = await provider.GetAccessTokenAsync();
        await provider.InvalidateAsync();
        var b = await provider.GetAccessTokenAsync();

        Assert.Equal("first", a);
        Assert.Equal("second", b);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ConcurrentCallers_ShareSingleFetch()
    {
        var fetchCount = 0;
        var handler = new RecordingHandler().RespondWith(req =>
        {
            Interlocked.Increment(ref fetchCount);
            Thread.Sleep(50); // hold the gate so callers stack up
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"access_token\":\"shared\",\"expires_in\":3600}",
                    System.Text.Encoding.UTF8, "application/json"),
            };
        });
        var provider = new OAuth2TokenProvider(TokenEndpoint, "c", "s", scope: null, new HttpClient(handler));

        var tasks = new Task<string>[8];
        for (var i = 0; i < tasks.Length; i++) tasks[i] = provider.GetAccessTokenAsync();
        var tokens = await Task.WhenAll(tasks);

        Assert.All(tokens, t => Assert.Equal("shared", t));
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task GetAccessTokenAsync_NonSuccess_ThrowsHttpRequestException()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_client\"}");
        var provider = new OAuth2TokenProvider(TokenEndpoint, "c", "s", scope: null, new HttpClient(handler));

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetAccessTokenAsync());
    }

    [Fact]
    public async Task GetAccessTokenAsync_EmptyAccessToken_Throws()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK, "{\"access_token\":\"\",\"expires_in\":3600}");
        var provider = new OAuth2TokenProvider(TokenEndpoint, "c", "s", scope: null, new HttpClient(handler));

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetAccessTokenAsync());
    }

    [Fact]
    public async Task RefreshSkew_ForcesRefetch_BeforeAbsoluteExpiry()
    {
        var handler = new RecordingHandler()
            .RespondWith(HttpStatusCode.OK, "{\"access_token\":\"short\",\"expires_in\":30}") // 30s < 60s skew → always invalid
            .RespondWith(HttpStatusCode.OK, "{\"access_token\":\"again\",\"expires_in\":3600}");
        var provider = new OAuth2TokenProvider(TokenEndpoint, "c", "s", scope: null, new HttpClient(handler));

        await provider.GetAccessTokenAsync();
        await provider.GetAccessTokenAsync();

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Scope_WhenProvided_IsIncludedInRequestBody()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK,
            "{\"access_token\":\"abc\",\"expires_in\":3600}");
        var provider = new OAuth2TokenProvider(TokenEndpoint, "client", "secret", scope: "api.read api.write", new HttpClient(handler));

        await provider.GetAccessTokenAsync();

        Assert.Contains("scope=api.read+api.write", handler.RequestBodies[^1]);
    }

    [Fact]
    public async Task Scope_WhenNull_IsOmittedFromRequestBody()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK,
            "{\"access_token\":\"abc\",\"expires_in\":3600}");
        var provider = new OAuth2TokenProvider(TokenEndpoint, "client", "secret", scope: null, new HttpClient(handler));

        await provider.GetAccessTokenAsync();

        Assert.DoesNotContain("scope=", handler.RequestBodies[^1]);
    }

    [Fact]
    public async Task RequestBody_HasExpectedClientCredentialsFields()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK, "{\"access_token\":\"x\",\"expires_in\":3600}");
        var provider = new OAuth2TokenProvider(TokenEndpoint, "myid", "mysecret", scope: null, new HttpClient(handler));

        await provider.GetAccessTokenAsync();

        var body = handler.RequestBodies[^1]!;
        Assert.Contains("grant_type=client_credentials", body);
        Assert.Contains("client_id=myid", body);
        Assert.Contains("client_secret=mysecret", body);
    }
}
