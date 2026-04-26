using System;
using NetworkBlast.OData;
using Xunit;

namespace NetworkBlast.Tests.OData;

public class ODataQueryTests
{
    private sealed record Customer(int Id, string Status, string Name, Address Address);
    private sealed record Address(string City);

    [Fact]
    public void Empty_ProducesEmptyDictionary() => Assert.Empty(ODataQuery.Empty.Build());

    [Fact]
    public void Filter_String_Sets_Dollar_Filter()
        => Assert.Equal("Status eq 'A'", ODataQuery.Filter("Status eq 'A'").Build()["$filter"]);

    [Fact]
    public void Filter_ODataFilter_Sets_Dollar_Filter()
        => Assert.Equal("Status eq 'A'", ODataQuery.Filter(ODataFilter.Eq("Status", "A")).Build()["$filter"]);

    [Fact]
    public void Filter_Linq_Sets_Dollar_Filter()
        => Assert.Equal("Status eq 'A'", ODataQuery.Filter<Customer>(c => c.Status == "A").Build()["$filter"]);

    [Fact]
    public void WithFilter_TwoCalls_AndMergesWithParens()
    {
        var built = ODataQuery
            .Filter("Status eq 'A'")
            .WithFilter("Age gt 18")
            .Build();
        Assert.Equal("(Status eq 'A') and (Age gt 18)", built["$filter"]);
    }

    [Fact]
    public void Select_String_CommaJoined()
        => Assert.Equal("Id,Name", ODataQuery.Empty.Select("Id", "Name").Build()["$select"]);

    [Fact]
    public void Select_AppendedAcrossMultipleCalls()
        => Assert.Equal("Id,Name", ODataQuery.Empty.Select("Id").Select("Name").Build()["$select"]);

    [Fact]
    public void Select_Typed_MemberAccess()
        => Assert.Equal("Id,Name", ODataQuery.Empty.Select<Customer>(c => c.Id, c => c.Name).Build()["$select"]);

    [Fact]
    public void Select_Typed_NestedMember_RendersWithSlash()
        => Assert.Equal("Address/City", ODataQuery.Empty.Select<Customer>(c => c.Address.City).Build()["$select"]);

    [Fact]
    public void Expand_CommaJoined()
        => Assert.Equal("Address,Orders", ODataQuery.Empty.Expand("Address", "Orders").Build()["$expand"]);

    [Fact]
    public void OrderBy_String_Ascending()
        => Assert.Equal("Name", ODataQuery.Empty.OrderBy("Name").Build()["$orderby"]);

    [Fact]
    public void OrderBy_Descending_AppendsSpaceDesc()
        => Assert.Equal("Name desc", ODataQuery.Empty.OrderBy("Name", descending: true).Build()["$orderby"]);

    [Fact]
    public void OrderByDescending_String()
        => Assert.Equal("Name desc", ODataQuery.Empty.OrderByDescending("Name").Build()["$orderby"]);

    [Fact]
    public void OrderBy_MultipleClauses_CommaJoined_AsThenBy()
        => Assert.Equal("Name,Id desc",
            ODataQuery.Empty.OrderBy("Name").OrderByDescending("Id").Build()["$orderby"]);

    [Fact]
    public void OrderBy_Typed() => Assert.Equal("Name", ODataQuery.Empty.OrderBy<Customer>(c => c.Name).Build()["$orderby"]);

    [Fact]
    public void Top_Sets_Dollar_Top() => Assert.Equal("50", ODataQuery.Empty.Top(50).Build()["$top"]);

    [Fact]
    public void Top_Negative_Throws() => Assert.Throws<ArgumentOutOfRangeException>(() => ODataQuery.Empty.Top(-1));

    [Fact]
    public void Skip_Sets_Dollar_Skip() => Assert.Equal("100", ODataQuery.Empty.Skip(100).Build()["$skip"]);

    [Fact]
    public void Skip_Negative_Throws() => Assert.Throws<ArgumentOutOfRangeException>(() => ODataQuery.Empty.Skip(-1));

    [Fact]
    public void Search_Sets_Dollar_Search() => Assert.Equal("hello", ODataQuery.Empty.Search("hello").Build()["$search"]);

    [Fact]
    public void Count_True_AddsDollarCount()
    {
        var built = ODataQuery.Empty.Count().Build();
        Assert.Equal("true", built["$count"]);
    }

    [Fact]
    public void Count_FalseFromTrue_RemovesDollarCount()
    {
        var built = ODataQuery.Empty.Count().Count(false).Build();
        Assert.False(built.ContainsKey("$count"));
    }

    [Fact]
    public void Build_OmitsUnsetFields()
    {
        var built = ODataQuery.Empty.Top(1).Build();
        Assert.Single(built);
        Assert.True(built.ContainsKey("$top"));
    }

    [Fact]
    public void ToRequestOptions_CarriesQueryDictionary()
    {
        var opts = ODataQuery.Empty.Top(5).ToRequestOptions();
        Assert.NotNull(opts.Query);
        Assert.Equal("5", opts.Query!["$top"]);
    }

    [Fact]
    public void Records_AreImmutable_ChainReturnsNewInstances()
    {
        var a = ODataQuery.Empty.Top(1);
        var b = a.Top(2);
        Assert.NotEqual(a, b);
        Assert.Equal("1", a.Build()["$top"]);
        Assert.Equal("2", b.Build()["$top"]);
    }

    [Fact]
    public void FullCompose_AllOptionsSurfaceInDict()
    {
        var dict = ODataQuery
            .Filter<Customer>(c => c.Status == "A")
            .Select<Customer>(c => c.Id, c => c.Name)
            .Expand("Orders")
            .OrderBy<Customer>(c => c.Name)
            .OrderByDescending<Customer>(c => c.Id)
            .Top(50)
            .Skip(100)
            .Search("text")
            .Count()
            .Build();

        Assert.Equal("Status eq 'A'", dict["$filter"]);
        Assert.Equal("Id,Name", dict["$select"]);
        Assert.Equal("Orders", dict["$expand"]);
        Assert.Equal("Name,Id desc", dict["$orderby"]);
        Assert.Equal("50", dict["$top"]);
        Assert.Equal("100", dict["$skip"]);
        Assert.Equal("text", dict["$search"]);
        Assert.Equal("true", dict["$count"]);
    }
}
