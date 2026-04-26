using System;
using System.Linq.Expressions;
using NetworkBlast.OData;
using Xunit;

namespace NetworkBlast.Tests.OData;

public class ODataLinqTests
{
    private sealed record Customer(int Id, string Status, string Name, int Age, bool IsActive, DateTimeOffset CreatedOn, Guid Token, Address Address);
    private sealed record Address(string City, string Country);

    [Fact] public void Equal_String()       => Assert.Equal("Status eq 'Active'", ODataFilter.For<Customer>(c => c.Status == "Active").Render());
    [Fact] public void NotEqual_String()    => Assert.Equal("Status ne 'Deleted'", ODataFilter.For<Customer>(c => c.Status != "Deleted").Render());
    [Fact] public void GreaterThan_Int()    => Assert.Equal("Age gt 18", ODataFilter.For<Customer>(c => c.Age > 18).Render());
    [Fact] public void LessThan_Int()       => Assert.Equal("Age lt 65", ODataFilter.For<Customer>(c => c.Age < 65).Render());
    [Fact] public void GreaterOrEqual_Int() => Assert.Equal("Age ge 18", ODataFilter.For<Customer>(c => c.Age >= 18).Render());
    [Fact] public void LessOrEqual_Int()    => Assert.Equal("Age le 65", ODataFilter.For<Customer>(c => c.Age <= 65).Render());

    [Fact]
    public void AndAlso_WrapsBothSidesInParens()
        => Assert.Equal("(Status eq 'Active' and Age gt 18)",
            ODataFilter.For<Customer>(c => c.Status == "Active" && c.Age > 18).Render());

    [Fact]
    public void OrElse_WrapsBothSidesInParens()
        => Assert.Equal("(Status eq 'A' or Status eq 'B')",
            ODataFilter.For<Customer>(c => c.Status == "A" || c.Status == "B").Render());

    [Fact]
    public void NotUnary_WrapsInner()
        => Assert.Equal("not (Status eq 'Deleted')",
            ODataFilter.For<Customer>(c => !(c.Status == "Deleted")).Render());

    [Fact]
    public void ClosureCapture_String_IsEvaluatedAndQuoted()
    {
        var name = "Active";
        Assert.Equal("Status eq 'Active'", ODataFilter.For<Customer>(c => c.Status == name).Render());
    }

    [Fact]
    public void ClosureCapture_DateTimeOffset_RendersAsIso()
    {
        var when = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var rendered = ODataFilter.For<Customer>(c => c.CreatedOn > when).Render();
        Assert.StartsWith("CreatedOn gt 2024-01-15", rendered);
    }

    [Fact]
    public void ClosureCapture_Guid_RendersBare()
    {
        var token = Guid.Parse("12345678-1234-1234-1234-123456789012");
        Assert.Equal("Token eq 12345678-1234-1234-1234-123456789012", ODataFilter.For<Customer>(c => c.Token == token).Render());
    }

    [Fact]
    public void NestedMember_RendersWithSlash()
        => Assert.Equal("Address/City eq 'NYC'",
            ODataFilter.For<Customer>(c => c.Address.City == "NYC").Render());

    [Fact]
    public void BareBoolMember_RendersAsEqTrueLowercase()
        => Assert.Equal("IsActive eq true", ODataFilter.For<Customer>(c => c.IsActive).Render());

    [Fact] public void StringContains()   => Assert.Equal("contains(Name, 'foo')",   ODataFilter.For<Customer>(c => c.Name.Contains("foo")).Render());
    [Fact] public void StringStartsWith() => Assert.Equal("startswith(Name, 'foo')", ODataFilter.For<Customer>(c => c.Name.StartsWith("foo")).Render());
    [Fact] public void StringEndsWith()   => Assert.Equal("endswith(Name, 'foo')",   ODataFilter.For<Customer>(c => c.Name.EndsWith("foo")).Render());

    [Fact]
    public void FlippedSides_ConstantOnLeft_FlipsOperator()
        => Assert.Equal("Age gt 18", ODataFilter.For<Customer>(c => 18 < c.Age).Render());

    [Fact]
    public void FlippedSides_GreaterOrEqual()
        => Assert.Equal("Age le 65", ODataFilter.For<Customer>(c => 65 >= c.Age).Render());

    [Fact]
    public void DeepComposition_PreservesPrecedence()
    {
        var min = 18;
        var rendered = ODataFilter.For<Customer>(c =>
            (c.Status == "Active" && c.Age >= min) || c.Name.StartsWith("VIP-")).Render();
        Assert.Equal("((Status eq 'Active' and Age ge 18) or startswith(Name, 'VIP-'))", rendered);
    }

    [Fact]
    public void UnsupportedMethodCall_Throws()
    {
        Expression<Func<Customer, bool>> bad = c => c.Name.IndexOf("x") > 0;
        Assert.Throws<NotSupportedException>(() => ODataFilter.For(bad));
    }

    [Fact]
    public void UnsupportedComparison_BothSidesConstant_Throws()
    {
        Expression<Func<Customer, bool>> bad = c => 1 == 2;
        Assert.Throws<NotSupportedException>(() => ODataFilter.For(bad));
    }
}
