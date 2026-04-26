using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NetworkBlast.Auth;
using NetworkBlast.Tests.Support;
using Xunit;

namespace NetworkBlast.Tests.Auth;

public class OAuth2DelegatingHandlerTests
{
    private static readonly Uri TokenEndpoint = new("https://login.example.test/oauth2/token");

    [Fact]
    public async Task SendAsync_AttachesBearerHeaderFromProvider()
    {
        var tokenHandler = new RecordingHandler()
            .RespondWith(HttpStatusCode.OK, "{\"access_token\":\"hello\",\"expires_in\":3600}");
        var apiHandler = new RecordingHandler().RespondWith(HttpStatusCode.OK);
        var provider = new OAuth2TokenProvider(TokenEndpoint, "c", "s", null, new HttpClient(tokenHandler));
        var oauth = new OAuth2DelegatingHandler(provider, apiHandler);
        var client = new HttpClient(oauth);

        await client.GetAsync("https://api.example.test/ping");

        Assert.Equal("Bearer", apiHandler.LastRequest.Headers.Authorization?.Scheme);
        Assert.Equal("hello", apiHandler.LastRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task SendAsync_OnUnauthorized_InvalidatesAndRetriesOnce()
    {
        var tokenHandler = new RecordingHandler()
            .RespondWith(HttpStatusCode.OK, "{\"access_token\":\"first\",\"expires_in\":3600}")
            .RespondWith(HttpStatusCode.OK, "{\"access_token\":\"second\",\"expires_in\":3600}");
        var apiHandler = new RecordingHandler()
            .RespondWith(HttpStatusCode.Unauthorized)
            .RespondWith(HttpStatusCode.OK);

        var provider = new OAuth2TokenProvider(TokenEndpoint, "c", "s", null, new HttpClient(tokenHandler));
        var oauth = new OAuth2DelegatingHandler(provider, apiHandler);
        var client = new HttpClient(oauth);

        var response = await client.GetAsync("https://api.example.test/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, apiHandler.Requests.Count);
        Assert.Equal("first", apiHandler.Requests[0].Headers.Authorization?.Parameter);
        Assert.Equal("second", apiHandler.Requests[1].Headers.Authorization?.Parameter);
        Assert.Equal(2, tokenHandler.Requests.Count);
    }

    [Fact]
    public async Task SendAsync_OnRepeated401_DoesNotLoopForever()
    {
        var tokenHandler = new RecordingHandler()
            .RespondWith(HttpStatusCode.OK, "{\"access_token\":\"a\",\"expires_in\":3600}")
            .RespondWith(HttpStatusCode.OK, "{\"access_token\":\"b\",\"expires_in\":3600}");
        var apiHandler = new RecordingHandler()
            .RespondWith(HttpStatusCode.Unauthorized)
            .RespondWith(HttpStatusCode.Unauthorized);

        var provider = new OAuth2TokenProvider(TokenEndpoint, "c", "s", null, new HttpClient(tokenHandler));
        var oauth = new OAuth2DelegatingHandler(provider, apiHandler);
        var client = new HttpClient(oauth);

        var response = await client.GetAsync("https://api.example.test/ping");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2, apiHandler.Requests.Count); // exactly two attempts
    }

    [Fact]
    public async Task SendAsync_NonAuthFailure_PassesThrough_NoRetry()
    {
        var tokenHandler = new RecordingHandler()
            .RespondWith(HttpStatusCode.OK, "{\"access_token\":\"x\",\"expires_in\":3600}");
        var apiHandler = new RecordingHandler().RespondWith(HttpStatusCode.InternalServerError);

        var provider = new OAuth2TokenProvider(TokenEndpoint, "c", "s", null, new HttpClient(tokenHandler));
        var oauth = new OAuth2DelegatingHandler(provider, apiHandler);
        var client = new HttpClient(oauth);

        var response = await client.GetAsync("https://api.example.test/ping");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Single(apiHandler.Requests);
    }
}
