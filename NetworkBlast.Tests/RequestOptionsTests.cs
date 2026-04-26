using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NetworkBlast;
using NetworkBlast.Tests.Support;
using Xunit;

namespace NetworkBlast.Tests;

public class RequestOptionsTests
{
    private static NetClient BuildClient(RecordingHandler handler) =>
        NetClient.Anonymous("https://example.test/", new HttpClient(handler));

    [Fact]
    public async Task Query_AppendsAllPairsAsEscapedQueryString()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK);
        var client = BuildClient(handler);

        await client.GetAsync("search", Options.Query(("q", "hello world"), ("page", "2"), ("safe", "1")));

        var uri = handler.LastRequest.RequestUri!;
        Assert.Contains("q=hello%20world", uri.Query);
        Assert.Contains("page=2", uri.Query);
        Assert.Contains("safe=1", uri.Query);
    }

    [Fact]
    public async Task Query_NullValues_AreSkipped()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK);
        var client = BuildClient(handler);

        await client.GetAsync("search", Options.Query(("q", "x"), ("ignored", null)));

        var query = handler.LastRequest.RequestUri!.Query;
        Assert.Contains("q=x", query);
        Assert.DoesNotContain("ignored", query);
    }

    [Fact]
    public async Task Query_PreservesExistingQueryStringInPath()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK);
        var client = BuildClient(handler);

        await client.GetAsync("search?source=cli", Options.Query(("q", "x")));

        var query = handler.LastRequest.RequestUri!.Query;
        Assert.Contains("source=cli", query);
        Assert.Contains("q=x", query);
    }

    [Fact]
    public async Task Headers_AreInjectedOnRequest_NotOnDefaultHeaders()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK);
        var client = BuildClient(handler);

        await client.GetAsync("ping", Options.Headers(("X-Trace-Id", "abc-123"), ("X-Tenant", "acme")));

        Assert.Equal("abc-123", handler.LastRequest.Headers.GetValues("X-Trace-Id").Single());
        Assert.Equal("acme", handler.LastRequest.Headers.GetValues("X-Tenant").Single());
    }

    [Fact]
    public async Task AddQuery_And_AddHeader_Compose_PreservingPriorEntries()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK);
        var client = BuildClient(handler);

        var opts = new RequestOptions()
            .AddQuery("page", "1")
            .AddQuery("size", "50")
            .AddHeader("X-Trace-Id", "abc")
            .AddHeader("X-Tenant", "acme");

        await client.GetAsync("ping", opts);

        var q = handler.LastRequest.RequestUri!.Query;
        Assert.Contains("page=1", q);
        Assert.Contains("size=50", q);
        Assert.Equal("abc", handler.LastRequest.Headers.GetValues("X-Trace-Id").Single());
        Assert.Equal("acme", handler.LastRequest.Headers.GetValues("X-Tenant").Single());
    }

    [Fact]
    public async Task PerRequestOptions_DoNotLeakToSubsequentRequests()
    {
        var handler = new RecordingHandler()
            .RespondWith(HttpStatusCode.OK)
            .RespondWith(HttpStatusCode.OK);
        var client = BuildClient(handler);

        await client.GetAsync("a", Options.Headers(("X-Once", "yes")));
        await client.GetAsync("b");

        Assert.True(handler.Requests[0].Headers.Contains("X-Once"));
        Assert.False(handler.Requests[1].Headers.Contains("X-Once"));
    }
}

internal static class TestEnumerableExtensions
{
    public static T Single<T>(this IEnumerable<T> source)
    {
        using var enumerator = source.GetEnumerator();
        if (!enumerator.MoveNext()) throw new InvalidOperationException("Sequence was empty");
        var first = enumerator.Current;
        if (enumerator.MoveNext()) throw new InvalidOperationException("Sequence had more than one element");
        return first;
    }
}
