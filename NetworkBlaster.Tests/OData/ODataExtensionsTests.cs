using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NetworkBlaster;
using NetworkBlaster.OData;
using NetworkBlaster.Tests.Support;
using Xunit;

namespace NetworkBlaster.Tests.OData;

public class ODataExtensionsTests
{
    private sealed record Customer(int Id, string Name);

    private static NetClient BuildClient(RecordingHandler handler) =>
        NetClient.Anonymous("https://example.test/", new HttpClient(handler));

    [Fact]
    public async Task GetODataAsync_DeserializesValueArray()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK,
            "{\"value\":[{\"id\":1,\"name\":\"a\"},{\"id\":2,\"name\":\"b\"}]}");
        var client = BuildClient(handler);

        var page = await client.GetODataAsync<Customer>("Customers");

        Assert.Equal(2, page.Value.Count);
        Assert.Equal("a", page.Value[0].Name);
        Assert.Equal("b", page.Value[1].Name);
    }

    [Fact]
    public async Task GetODataAsync_PicksUpODataAnnotations()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK,
            "{\"@odata.context\":\"https://example.test/$metadata#Customers\"," +
             "\"@odata.count\":12345," +
             "\"@odata.nextLink\":\"https://example.test/Customers?$skiptoken=xyz\"," +
             "\"value\":[]}");
        var client = BuildClient(handler);

        var page = await client.GetODataAsync<Customer>("Customers");

        Assert.Equal(12345, page.Count);
        Assert.Equal("https://example.test/Customers?$skiptoken=xyz", page.NextLink);
        Assert.Equal("https://example.test/$metadata#Customers", page.Context);
    }

    [Fact]
    public async Task GetODataAsync_EmptyValue_ReturnsEmptyList()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK, "{\"value\":[]}");
        var client = BuildClient(handler);

        var page = await client.GetODataAsync<Customer>("Customers");

        Assert.Empty(page.Value);
    }

    [Fact]
    public async Task GetODataAsync_MissingValueField_ReturnsEmptyList()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK, "{}");
        var client = BuildClient(handler);

        var page = await client.GetODataAsync<Customer>("Customers");

        Assert.Empty(page.Value);
    }

    [Fact]
    public async Task GetODataAsync_AppliesODataQueryAsQueryString()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK, "{\"value\":[]}");
        var client = BuildClient(handler);

        var query = ODataQuery
            .Filter<Customer>(c => c.Name == "x")
            .Top(10);

        await client.GetODataAsync<Customer>("Customers", query);

        var sent = handler.LastRequest.RequestUri!.Query;
        Assert.Contains("%24filter=Name%20eq%20%27x%27", sent);
        Assert.Contains("%24top=10", sent);
    }

    [Fact]
    public async Task GetODataAsync_MergesODataQueryWithCallerRequestOptions()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK, "{\"value\":[]}");
        var client = BuildClient(handler);

        var caller = Options.Headers(("X-Tenant", "acme")) with
        {
            Query = new Dictionary<string, string?> { ["api-version"] = "2024-01-01" }
        };

        await client.GetODataAsync<Customer>("Customers", ODataQuery.Empty.Top(5), caller);

        var query = handler.LastRequest.RequestUri!.Query;
        Assert.Contains("api-version=2024-01-01", query);
        Assert.Contains("%24top=5", query);
        Assert.Equal("acme", string.Join(",", handler.LastRequest.Headers.GetValues("X-Tenant")));
    }

    [Fact]
    public async Task GetODataItemAsync_DeserializesEntity_IgnoresContextAnnotation()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK,
            "{\"@odata.context\":\"...\",\"id\":42,\"name\":\"answer\"}");
        var client = BuildClient(handler);

        var item = await client.GetODataItemAsync<Customer>("Customers(42)");

        Assert.NotNull(item);
        Assert.Equal(42, item!.Id);
        Assert.Equal("answer", item.Name);
    }
}
