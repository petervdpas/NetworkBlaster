using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using NetworkBlast;
using NetworkBlast.Tests.Support;
using Xunit;

namespace NetworkBlast.Tests;

public class RetryTests
{
    private static readonly TimeSpan FastDelay = TimeSpan.FromMilliseconds(1);

    private static NetClient BuildClient(RecordingHandler handler, int retries = 3) =>
        new(
            (c, k, ct) => Task.FromResult(k == "baseUrl" ? "https://example.test/" : string.Empty),
            "x",
            new HttpClient(handler),
            defaultRetryCount: retries,
            defaultRetryBaseDelay: FastDelay);

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.RequestTimeout)]      // 408
    [InlineData(HttpStatusCode.TooManyRequests)]     // 429
    public async Task TransientStatuses_AreRetried_UntilSuccess(HttpStatusCode transient)
    {
        var handler = new RecordingHandler()
            .RespondWith(transient)
            .RespondWith(transient)
            .RespondWith(HttpStatusCode.OK);
        var client = BuildClient(handler);

        var response = await client.GetAsync("ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Retry_ExhaustsBudget_ReturnsLastFailingResponse()
    {
        var handler = new RecordingHandler()
            .RespondWith(HttpStatusCode.BadGateway)
            .RespondWith(HttpStatusCode.BadGateway)
            .RespondWith(HttpStatusCode.BadGateway);
        var client = BuildClient(handler, retries: 2); // 1 initial + 2 retries = 3 total

        var response = await client.GetAsync("ping");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Retry_OnHttpRequestException_RecoversWhenLaterAttemptSucceeds()
    {
        var handler = new RecordingHandler()
            .ThrowsOnce(new HttpRequestException("connection refused"))
            .RespondWith(HttpStatusCode.OK);
        var client = BuildClient(handler);

        var response = await client.GetAsync("ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Retry_ExhaustionAfterAllExceptions_ThrowsLastException()
    {
        var handler = new RecordingHandler()
            .ThrowsOnce(new HttpRequestException("a"))
            .ThrowsOnce(new HttpRequestException("b"))
            .ThrowsOnce(new HttpRequestException("c"));
        var client = BuildClient(handler, retries: 2);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("ping"));
        Assert.Equal("c", ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task NonTransient4xx_IsNotRetried(HttpStatusCode status)
    {
        var handler = new RecordingHandler().RespondWith(status);
        var client = BuildClient(handler, retries: 5);

        var response = await client.GetAsync("ping");

        Assert.Equal(status, response.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RetryAfter_DeltaSeconds_IsHonored()
    {
        var handler = new RecordingHandler()
            .RespondWith(_ =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                resp.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(20));
                return resp;
            })
            .RespondWith(HttpStatusCode.OK);
        var client = BuildClient(handler);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await client.GetAsync("ping");
        sw.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(sw.ElapsedMilliseconds >= 15, $"expected >=15ms wait, got {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task PerCallRetryOverride_BeatsClientDefault()
    {
        var handler = new RecordingHandler()
            .RespondWith(HttpStatusCode.BadGateway)
            .RespondWith(HttpStatusCode.BadGateway)
            .RespondWith(HttpStatusCode.OK);
        var client = new NetClient(
            (c, k, ct) => Task.FromResult(k == "baseUrl" ? "https://example.test/" : string.Empty),
            "x",
            new HttpClient(handler),
            defaultRetryCount: 0,                              // off by default
            defaultRetryBaseDelay: FastDelay);

        var response = await client.GetAsync("ping", Options.Retries(2, FastDelay));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task RetryDisabledByDefault_TransientStatusReturnedImmediately()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.BadGateway);
        var client = new NetClient(
            (c, k, ct) => Task.FromResult(k == "baseUrl" ? "https://example.test/" : string.Empty),
            "x",
            new HttpClient(handler));

        var response = await client.GetAsync("ping");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Single(handler.Requests);
    }
}
