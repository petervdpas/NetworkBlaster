using System;
using System.Globalization;

namespace NetworkBlaster.OData;

/// <summary>
/// Renders .NET values as OData v4 literals for use inside a <c>$filter</c> or other
/// query option. Used by <see cref="ODataFilter"/> internally; also exposed for callers
/// that need to compose raw filter strings.
/// </summary>
public static class ODataLiteral
{
    /// <summary>Renders a string value with OData single-quote doubling: <c>O'Brien</c> → <c>'O''Brien'</c>.</summary>
    public static string String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "'" + value.Replace("'", "''") + "'";
    }

    /// <summary>Renders a <see cref="DateTimeOffset"/> as ISO-8601 (no quotes per OData v4).</summary>
    public static string DateTimeOffset(DateTimeOffset value)
        => value.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture);

    /// <summary>Renders a <see cref="DateTime"/> in UTC ISO-8601 form.</summary>
    public static string DateTime(DateTime value)
        => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    /// <summary>Renders a <see cref="DateOnly"/>.</summary>
    public static string Date(DateOnly value)
        => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Renders a <see cref="TimeOnly"/>.</summary>
    public static string Time(TimeOnly value)
        => value.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>Renders a <see cref="System.Guid"/> bare (OData v4 — no surrounding quotes).</summary>
    public static string Guid(Guid value) => value.ToString("D");

    /// <summary>Renders a boolean as <c>true</c>/<c>false</c>.</summary>
    public static string Bool(bool value) => value ? "true" : "false";

    /// <summary>Renders any <see cref="IFormattable"/> number with invariant culture.</summary>
    public static string Number(IFormattable value)
        => value.ToString(null, CultureInfo.InvariantCulture);

    /// <summary>
    /// Auto-renders an arbitrary .NET value using the appropriate OData literal form.
    /// Falls back to a quoted string for unknown types.
    /// </summary>
    public static string From(object? value) => value switch
    {
        null               => "null",
        string s           => String(s),
        bool b             => Bool(b),
        DateTimeOffset dto => DateTimeOffset(dto),
        DateTime dt        => DateTime(dt),
        DateOnly d         => Date(d),
        TimeOnly t         => Time(t),
        Guid g             => Guid(g),
        Enum e             => String(e.ToString()),
        IFormattable n when IsNumeric(n) => Number(n),
        _                  => String(value.ToString() ?? string.Empty),
    };

    private static bool IsNumeric(object value) => value
        is byte or sbyte
        or short or ushort
        or int or uint
        or long or ulong
        or float or double or decimal;
}
