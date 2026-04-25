using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NetworkBlaster;
using NetworkBlaster.Auth;
using NetworkBlaster.Tests.Support;
using Xunit;

namespace NetworkBlaster.Tests.Auth;

public class OAuth2EndToEndTests
{
    private static NetClient BuildClient(RecordingHandler tokenHandler, RecordingHandler apiHandler)
    {
        var provider = new OAuth2TokenProvider(
            new Uri("https://login.example.test/oauth2/token"),
            "client", "secret", null, new HttpClient(tokenHandler));
        var oauthHandler = new OAuth2DelegatingHandler(provider, apiHandler);
        var apiClient = new HttpClient(oauthHandler);

        return new NetClient(
            (_, k, _) => Task.FromResult(k == "baseUrl" ? "https://api.example.test/" : string.Empty),
            "service",
            apiClient);
    }

    [Fact]
    public async Task NetClient_OAuth2_AppliesBearerOnEveryRequest_FetchingTokenOnce()
    {
        var tokenHandler = new RecordingHandler()
            .RespondWith(HttpStatusCode.OK, "{\"access_token\":\"abc\",\"expires_in\":3600}");
        var apiHandler = new RecordingHandler()
            .RespondWith(HttpStatusCode.OK)
            .RespondWith(HttpStatusCode.OK)
            .RespondWith(HttpStatusCode.OK);

        var client = BuildClient(tokenHandler, apiHandler);

        await client.GetAsync("a");
        await client.GetAsync("b");
        await client.GetAsync("c");

        Assert.Single(tokenHandler.Requests);
        Assert.Equal(3, apiHandler.Requests.Count);
        Assert.All(apiHandler.Requests, r => Assert.Equal("abc", r.Headers.Authorization?.Parameter));
    }

    [Fact]
    public async Task NetClient_OAuth2_RefreshesOn401_ThenSucceeds()
    {
        var tokenHandler = new RecordingHandler()
            .RespondWith(HttpStatusCode.OK, "{\"access_token\":\"stale\",\"expires_in\":3600}")
            .RespondWith(HttpStatusCode.OK, "{\"access_token\":\"fresh\",\"expires_in\":3600}");
        var apiHandler = new RecordingHandler()
            .RespondWith(HttpStatusCode.Unauthorized)
            .RespondWith(HttpStatusCode.OK);

        var client = BuildClient(tokenHandler, apiHandler);
        var response = await client.GetAsync("ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("stale", apiHandler.Requests[0].Headers.Authorization?.Parameter);
        Assert.Equal("fresh", apiHandler.Requests[1].Headers.Authorization?.Parameter);
    }
}
