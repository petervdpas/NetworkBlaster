using System;
using NetworkBlaster.OData;
using Xunit;

namespace NetworkBlaster.Tests.OData;

public class ODataLiteralTests
{
    [Fact] public void String_NoApostrophe_WrapsInSingleQuotes() => Assert.Equal("'Active'", ODataLiteral.String("Active"));
    [Fact] public void String_SingleApostrophe_DoublesIt()      => Assert.Equal("'O''Brien'", ODataLiteral.String("O'Brien"));
    [Fact] public void String_MultipleApostrophes_AllDoubled()   => Assert.Equal("'a''b''c'", ODataLiteral.String("a'b'c"));
    [Fact] public void String_Empty_RendersAsTwoQuotes()         => Assert.Equal("''", ODataLiteral.String(""));
    [Fact] public void String_Null_Throws() => Assert.Throws<ArgumentNullException>(() => ODataLiteral.String(null!));

    [Fact]
    public void DateTimeOffset_FormatsAsIsoWithOffset()
    {
        var dto = new DateTimeOffset(2024, 1, 15, 12, 30, 45, 123, TimeSpan.FromHours(2));
        Assert.Equal("2024-01-15T12:30:45.123+02:00", ODataLiteral.DateTimeOffset(dto));
    }

    [Fact]
    public void DateTime_ConvertsToUtcAndAppendsZ()
    {
        var local = new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal("2024-06-01T10:00:00.000Z", ODataLiteral.DateTime(local));
    }

    [Fact] public void Date_FormatsAsYyyyMmDd() => Assert.Equal("2024-01-15", ODataLiteral.Date(new DateOnly(2024, 1, 15)));
    [Fact] public void Time_FormatsHmsMillis()  => Assert.Equal("13:45:01.234", ODataLiteral.Time(new TimeOnly(13, 45, 1, 234)));

    [Fact]
    public void Guid_RendersBareNoQuotes()
    {
        var g = Guid.Parse("12345678-1234-1234-1234-123456789012");
        Assert.Equal("12345678-1234-1234-1234-123456789012", ODataLiteral.Guid(g));
    }

    [Fact] public void Bool_True_RendersTrue()    => Assert.Equal("true",  ODataLiteral.Bool(true));
    [Fact] public void Bool_False_RendersFalse()  => Assert.Equal("false", ODataLiteral.Bool(false));

    [Fact] public void Number_Int()       => Assert.Equal("42",      ODataLiteral.Number(42));
    [Fact] public void Number_Long()      => Assert.Equal("9000000000", ODataLiteral.Number(9_000_000_000L));
    [Fact] public void Number_Double()    => Assert.Equal("3.14",    ODataLiteral.Number(3.14));
    [Fact] public void Number_Decimal()   => Assert.Equal("9.99",    ODataLiteral.Number(9.99m));
    [Fact] public void Number_InvariantCulture_NoLocaleComma() => Assert.Equal("1.5", ODataLiteral.Number(1.5));

    [Fact] public void From_Null_ReturnsNull()                  => Assert.Equal("null",     ODataLiteral.From(null));
    [Fact] public void From_String()                            => Assert.Equal("'x'",      ODataLiteral.From("x"));
    [Fact] public void From_Bool()                              => Assert.Equal("true",     ODataLiteral.From(true));
    [Fact] public void From_Int()                               => Assert.Equal("7",        ODataLiteral.From(7));
    [Fact] public void From_Long()                              => Assert.Equal("123",      ODataLiteral.From(123L));
    [Fact] public void From_Double()                            => Assert.Equal("2.5",      ODataLiteral.From(2.5));
    [Fact] public void From_Decimal()                           => Assert.Equal("1.1",      ODataLiteral.From(1.1m));
    [Fact] public void From_Guid()
    {
        var g = Guid.Parse("12345678-1234-1234-1234-123456789012");
        Assert.Equal("12345678-1234-1234-1234-123456789012", ODataLiteral.From(g));
    }
    [Fact] public void From_DateTimeOffset() => Assert.Contains("2024-01-15", ODataLiteral.From(new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero)));
    [Fact] public void From_DateOnly()       => Assert.Equal("2024-01-15", ODataLiteral.From(new DateOnly(2024, 1, 15)));
    [Fact] public void From_TimeOnly()       => Assert.Equal("12:00:00.000", ODataLiteral.From(new TimeOnly(12, 0, 0)));
    [Fact] public void From_Enum()           => Assert.Equal("'Active'", ODataLiteral.From(Status.Active));
    [Fact] public void From_UnknownType_FallsBackToQuotedToString() => Assert.Equal("'Mr Custom'", ODataLiteral.From(new CustomFormat()));

    private enum Status { Active, Disabled }
    private sealed class CustomFormat { public override string ToString() => "Mr Custom"; }
}
