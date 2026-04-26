using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NetworkBlast;
using NetworkBlast.OData;
using NetworkBlast.Tests.Support;
using Xunit;

namespace NetworkBlast.Tests.OData;

public class ODataRequestTests
{
    private sealed record Customer(int Id, string Status, string Name, int Age);

    private static NetClient BuildClient(RecordingHandler handler) =>
        NetClient.Anonymous("https://example.test/", new HttpClient(handler));

    [Fact]
    public void OData_EntryPoint_StartsEmptyQueryAtPath()
    {
        var client = BuildClient(new RecordingHandler());
        var request = client.OData<Customer>("Customers");

        Assert.Equal("Customers", request.Path);
        Assert.Equal(ODataQuery.Empty, request.Query);
    }

    [Fact]
    public void Where_LinqPredicate_IsTranslatedIntoFilter()
    {
        var request = BuildClient(new RecordingHandler())
            .OData<Customer>("Customers")
            .Where(c => c.Status == "Active");

        Assert.Equal("Status eq 'Active'", request.Query.Build()["$filter"]);
    }

    [Fact]
    public void MultipleWhereCalls_AreAndMerged()
    {
        var request = BuildClient(new RecordingHandler())
            .OData<Customer>("Customers")
            .Where(c => c.Status == "Active")
            .Where(c => c.Age > 18);

        Assert.Equal("(Status eq 'Active') and (Age gt 18)", request.Query.Build()["$filter"]);
    }

    [Fact]
    public void OrderBy_Then_ThenByDescending_ProducesCommaJoined()
    {
        var request = BuildClient(new RecordingHandler())
            .OData<Customer>("Customers")
            .OrderBy(c => c.Name)
            .ThenByDescending(c => c.Id);

        Assert.Equal("Name,Id desc", request.Query.Build()["$orderby"]);
    }

    [Fact]
    public void Select_Expand_Top_Skip_Search_Count_AllSurface()
    {
        var request = BuildClient(new RecordingHandler())
            .OData<Customer>("Customers")
            .Select(c => c.Id, c => c.Name)
            .Top(50)
            .Skip(100)
            .Search("vip")
            .WithCount();

        var dict = request.Query.Build();
        Assert.Equal("Id,Name", dict["$select"]);
        Assert.Equal("50", dict["$top"]);
        Assert.Equal("100", dict["$skip"]);
        Assert.Equal("vip", dict["$search"]);
        Assert.Equal("true", dict["$count"]);
    }

    [Fact]
    public async Task ToListAsync_MaterialisesAllPages()
    {
        var handler = new RecordingHandler()
            .RespondWith(HttpStatusCode.OK,
                "{\"value\":[{\"id\":1,\"status\":\"A\",\"name\":\"a\",\"age\":20}],\"@odata.nextLink\":\"https://example.test/Customers?p=2\"}")
            .RespondWith(HttpStatusCode.OK,
                "{\"value\":[{\"id\":2,\"status\":\"A\",\"name\":\"b\",\"age\":30}]}");

        var list = await BuildClient(handler).OData<Customer>("Customers").ToListAsync();

        Assert.Equal(2, list.Count);
        Assert.Equal("a", list[0].Name);
        Assert.Equal("b", list[1].Name);
    }

    [Fact]
    public async Task FirstPageAsync_ReturnsRawODataPage()
    {
        var handler = new RecordingHandler()
            .RespondWith(HttpStatusCode.OK,
                "{\"value\":[{\"id\":1,\"status\":\"A\",\"name\":\"a\",\"age\":20}],\"@odata.count\":99}");

        var page = await BuildClient(handler).OData<Customer>("Customers").WithCount().FirstPageAsync();

        Assert.Single(page.Value);
        Assert.Equal(99, page.Count);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_AppliesTop1AndReturnsFirst()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK,
            "{\"value\":[{\"id\":7,\"status\":\"A\",\"name\":\"x\",\"age\":40}]}");

        var first = await BuildClient(handler).OData<Customer>("Customers").FirstOrDefaultAsync();

        Assert.NotNull(first);
        Assert.Equal(7, first!.Id);
        Assert.Contains("%24top=1", handler.LastRequest.RequestUri!.Query);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_ReturnsDefault_WhenEmpty()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK, "{\"value\":[]}");

        var first = await BuildClient(handler).OData<Customer>("Customers").FirstOrDefaultAsync();

        Assert.Null(first);
    }

    [Fact]
    public async Task SingleOrDefaultAsync_OnZeroResults_ReturnsNull()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK, "{\"value\":[]}");
        var single = await BuildClient(handler).OData<Customer>("Customers").SingleOrDefaultAsync();
        Assert.Null(single);
    }

    [Fact]
    public async Task SingleOrDefaultAsync_OnOneResult_ReturnsEntity()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK,
            "{\"value\":[{\"id\":1,\"status\":\"A\",\"name\":\"x\",\"age\":40}]}");

        var single = await BuildClient(handler).OData<Customer>("Customers").SingleOrDefaultAsync();

        Assert.NotNull(single);
        Assert.Equal(1, single!.Id);
        Assert.Contains("%24top=2", handler.LastRequest.RequestUri!.Query);
    }

    [Fact]
    public async Task SingleOrDefaultAsync_OnMultipleResults_Throws()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK,
            "{\"value\":[" +
            "{\"id\":1,\"status\":\"A\",\"name\":\"x\",\"age\":40}," +
            "{\"id\":2,\"status\":\"A\",\"name\":\"y\",\"age\":41}]}");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BuildClient(handler).OData<Customer>("Customers").SingleOrDefaultAsync());
    }

    [Fact]
    public async Task CountAsync_ReturnsServerCount_AppliesCountAndTop0()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK,
            "{\"value\":[],\"@odata.count\":420}");

        var count = await BuildClient(handler).OData<Customer>("Customers").CountAsync();

        Assert.Equal(420, count);
        var query = handler.LastRequest.RequestUri!.Query;
        Assert.Contains("%24count=true", query);
        Assert.Contains("%24top=0", query);
    }

    [Fact]
    public async Task CountAsync_Throws_WhenServerMissingCount()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK, "{\"value\":[]}");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BuildClient(handler).OData<Customer>("Customers").CountAsync());
    }

    [Fact]
    public async Task FullChain_AppliesAllQueryParametersToRequestUri()
    {
        var handler = new RecordingHandler().RespondWith(HttpStatusCode.OK, "{\"value\":[]}");
        var client = BuildClient(handler);

        await client.OData<Customer>("Customers")
            .Where(c => c.Status == "Active")
            .Where(c => c.Age > 18)
            .OrderBy(c => c.Name)
            .ThenByDescending(c => c.Id)
            .Select(c => c.Id, c => c.Name)
            .Top(50)
            .ToListAsync();

        var query = handler.LastRequest.RequestUri!.Query;
        Assert.Contains("%24filter=", query);
        Assert.Contains("%24orderby=Name%2CId%20desc", query);
        Assert.Contains("%24select=Id%2CName", query);
        Assert.Contains("%24top=50", query);
    }
}
