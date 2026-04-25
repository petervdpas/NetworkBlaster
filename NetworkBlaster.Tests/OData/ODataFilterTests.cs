using System;
using NetworkBlaster.OData;
using Xunit;

namespace NetworkBlaster.Tests.OData;

public class ODataFilterTests
{
    [Fact] public void Eq_String()    => Assert.Equal("Status eq 'Active'", ODataFilter.Eq("Status", "Active").Render());
    [Fact] public void Eq_Int()       => Assert.Equal("Age eq 18", ODataFilter.Eq("Age", 18).Render());
    [Fact] public void Eq_Bool()      => Assert.Equal("IsActive eq true", ODataFilter.Eq("IsActive", true).Render());
    [Fact] public void Eq_Null()      => Assert.Equal("Note eq null", ODataFilter.Eq("Note", null).Render());
    [Fact] public void Eq_Guid()
    {
        var g = Guid.Parse("12345678-1234-1234-1234-123456789012");
        Assert.Equal("Id eq 12345678-1234-1234-1234-123456789012", ODataFilter.Eq("Id", g).Render());
    }
    [Fact] public void Ne() => Assert.Equal("X ne 1", ODataFilter.Ne("X", 1).Render());
    [Fact] public void Gt() => Assert.Equal("X gt 1", ODataFilter.Gt("X", 1).Render());
    [Fact] public void Lt() => Assert.Equal("X lt 1", ODataFilter.Lt("X", 1).Render());
    [Fact] public void Ge() => Assert.Equal("X ge 1", ODataFilter.Ge("X", 1).Render());
    [Fact] public void Le() => Assert.Equal("X le 1", ODataFilter.Le("X", 1).Render());

    [Fact] public void Contains()   => Assert.Equal("contains(Name, 'foo')",   ODataFilter.Contains  ("Name", "foo").Render());
    [Fact] public void StartsWith() => Assert.Equal("startswith(Name, 'foo')", ODataFilter.StartsWith("Name", "foo").Render());
    [Fact] public void EndsWith()   => Assert.Equal("endswith(Name, 'foo')",   ODataFilter.EndsWith  ("Name", "foo").Render());
    [Fact] public void StringFunction_EscapesApostropheInValue()
        => Assert.Equal("contains(Name, 'O''Brien')", ODataFilter.Contains("Name", "O'Brien").Render());

    [Fact] public void In_SingleValue_NoParens() => Assert.Equal("X eq 1", ODataFilter.In("X", 1).Render());
    [Fact] public void In_MultipleValues_OrJoinedAndParenthesised() => Assert.Equal("(X eq 1 or X eq 2 or X eq 3)", ODataFilter.In("X", 1, 2, 3).Render());
    [Fact] public void In_MixedTypes() => Assert.Equal("(X eq 'a' or X eq 1 or X eq null)", ODataFilter.In("X", "a", 1, null).Render());
    [Fact] public void In_EmptyValues_Throws() => Assert.Throws<ArgumentException>(() => ODataFilter.In("X"));

    [Fact] public void And_WrapsChildrenInParens()
        => Assert.Equal("(X eq 1 and Y eq 2)", ODataFilter.And(ODataFilter.Eq("X", 1), ODataFilter.Eq("Y", 2)).Render());

    [Fact] public void Or_WrapsChildrenInParens()
        => Assert.Equal("(X eq 1 or Y eq 2)", ODataFilter.Or(ODataFilter.Eq("X", 1), ODataFilter.Eq("Y", 2)).Render());

    [Fact] public void Not_WrapsInner() => Assert.Equal("not (X eq 1)", ODataFilter.Not(ODataFilter.Eq("X", 1)).Render());

    [Fact] public void OperatorAmpersand_EqualsAnd()
        => Assert.Equal("(X eq 1 and Y eq 2)", (ODataFilter.Eq("X", 1) & ODataFilter.Eq("Y", 2)).Render());

    [Fact] public void OperatorPipe_EqualsOr()
        => Assert.Equal("(X eq 1 or Y eq 2)", (ODataFilter.Eq("X", 1) | ODataFilter.Eq("Y", 2)).Render());

    [Fact] public void OperatorBang_EqualsNot() => Assert.Equal("not (X eq 1)", (!ODataFilter.Eq("X", 1)).Render());

    [Fact]
    public void NestedComposition_PreservesPrecedenceViaParens()
    {
        var filter = (ODataFilter.Eq("Status", "Active") & ODataFilter.Gt("Age", 18))
                     | ODataFilter.Eq("Pinned", true);
        Assert.Equal("((Status eq 'Active' and Age gt 18) or Pinned eq true)", filter.Render());
    }

    [Fact] public void Raw_PassesThroughUnchanged() => Assert.Equal("foo bar baz", ODataFilter.Raw("foo bar baz").Render());

    [Fact] public void ToString_EqualsRender() => Assert.Equal(ODataFilter.Eq("X", 1).Render(), ODataFilter.Eq("X", 1).ToString());
}
