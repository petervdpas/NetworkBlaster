using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NetworkBlaster;
using NetworkBlaster.OData;
using NetworkBlaster.Tests.Support;
using Xunit;

namespace NetworkBlaster.Tests.OData;

public class ODataPagingTests
{
    private sealed record Customer(int Id, string Name);

    private static NetClient BuildClient(RecordingHandler handler) =>
        NetClient.Anonymous("https://example.test/", new HttpClient(handler));

    [Fact]
    public async Task QueryODataAsync_FollowsNextLinkAcrossTwoPages()
    {
        var handler = new RecordingHandler()
            .RespondWith(HttpStatusCode.OK,
                "{\"value\":[{\"id\":1,\"name\":\"a\"}],\"@odata.nextLink\":\"https://example.test/Customers?$skiptoken=2\"}")
            .RespondWith(HttpStatusCode.OK,
                "{\"value\":[{\"id\":2,\"name\":\"b\"}]}");
        var client = BuildClient(handler);

        var collected = new List<Customer>();
        await foreach (var c in client.QueryODataAsync<Customer>("Customers"))
            collected.Add(c);

        Assert.Equal(2, collected.Count);
        Assert.Equal(1, collected[0].Id);
        Assert.Equal(2, collected[1].Id);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task QueryODataAsync_FollowsNextLinkAcrossThreePages()
    {
        var handler = new RecordingHandler()
            .RespondWith(HttpStatusCode.OK, "{\"value\":[{\"id\":1,\"name\":\"a\"}],\"@odata.nextLink\":\"https://example.test/Customers?p=2\"}")
            .RespondWith(HttpStatusCode.OK, "{\"value\":[{\"id\":2,\"name\":\"b\"}],\"@odata.nextLink\":\"https://example.test/Customers?p=3\"}")
            .RespondWith(HttpStatusCode.OK, "{\"value\":[{\"id\":3,\"name\":\"c\"}]}");
        var client = BuildClient(handler);

        var collected = new List<Customer>();
        await foreach (var c in client.QueryODataAsync<Customer>("Customers"))
            collected.Add(c);

        Assert.Equal(3, collected.Count);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task QueryODataAsync_StopsWhenNextLinkAbsent()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK,
            "{\"value\":[{\"id\":1,\"name\":\"a\"}]}");
        var client = BuildClient(handler);

        var count = 0;
        await foreach (var _ in client.QueryODataAsync<Customer>("Customers")) count++;

        Assert.Equal(1, count);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task QueryODataAsync_StopsWhenNextLinkExplicitlyNull()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK,
            "{\"value\":[{\"id\":1,\"name\":\"a\"}],\"@odata.nextLink\":null}");
        var client = BuildClient(handler);

        var count = 0;
        await foreach (var _ in client.QueryODataAsync<Customer>("Customers")) count++;

        Assert.Equal(1, count);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task QueryODataAsync_NextLinkRequest_UsesAbsoluteUrl()
    {
        var handler = new RecordingHandler()
            .RespondWith(HttpStatusCode.OK,
                "{\"value\":[],\"@odata.nextLink\":\"https://other.host/Customers?p=2\"}")
            .RespondWith(HttpStatusCode.OK, "{\"value\":[]}");
        var client = BuildClient(handler);

        await foreach (var _ in client.QueryODataAsync<Customer>("Customers")) { }

        Assert.Equal("https://other.host/Customers?p=2", handler.Requests[1].RequestUri!.ToString());
    }

    [Fact]
    public async Task QueryODataAsync_HonoursCancellationBetweenPages()
    {
        var handler = new RecordingHandler()
            .RespondWith(HttpStatusCode.OK,
                "{\"value\":[{\"id\":1,\"name\":\"a\"}],\"@odata.nextLink\":\"https://example.test/Customers?p=2\"}")
            .RespondWith(HttpStatusCode.OK, "{\"value\":[{\"id\":2,\"name\":\"b\"}]}");
        var client = BuildClient(handler);

        using var cts = new CancellationTokenSource();
        var collected = new List<Customer>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var c in client.QueryODataAsync<Customer>("Customers", cancellationToken: cts.Token))
            {
                collected.Add(c);
                cts.Cancel();
            }
        });

        Assert.Single(collected);
    }
}
