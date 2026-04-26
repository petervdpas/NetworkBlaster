using System;
using System.Collections.Generic;

namespace NetworkBlast;

/// <summary>
/// Static factories for the most common one-off <see cref="RequestOptions"/> shapes.
/// Compose multiple via record <c>with</c> expressions:
/// <code>
/// var opts = RequestOptions.Query(("page", "1"))
///     with { Headers = RequestOptions.Headers(("X-API-Key", "foo")).Headers };
/// </code>
/// </summary>
public static class Options
{
    /// <summary>One-off options carrying only a query-string.</summary>
    public static RequestOptions Query(params (string key, string? value)[] pairs)
        => new() { Query = ToDict(pairs) };

    /// <summary>One-off options carrying only request headers.</summary>
    public static RequestOptions Headers(params (string name, string? value)[] pairs)
        => new() { Headers = ToDict(pairs) };

    /// <summary>One-off options carrying only a timeout.</summary>
    public static RequestOptions Timeout(TimeSpan timeout)
        => new() { Timeout = timeout };

    /// <summary>One-off options enabling retry for a single call.</summary>
    public static RequestOptions Retries(int count, TimeSpan? baseDelay = null)
        => new() { RetryCount = count, RetryBaseDelay = baseDelay };

    private static Dictionary<string, string?> ToDict((string key, string? value)[] pairs)
    {
        var dict = new Dictionary<string, string?>(pairs.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in pairs) dict[key] = value;
        return dict;
    }
}

/// <summary>
/// Chainable extensions for incrementally building a <see cref="RequestOptions"/> instance.
/// </summary>
public static class RequestOptionsExtensions
{
    /// <summary>Adds a single query-string parameter, preserving any existing entries.</summary>
    public static RequestOptions AddQuery(this RequestOptions options, string name, string? value)
    {
        var dict = options.Query is null
            ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(options.Query, StringComparer.OrdinalIgnoreCase);
        dict[name] = value;
        return options with { Query = dict };
    }

    /// <summary>Adds a single header, preserving any existing entries.</summary>
    public static RequestOptions AddHeader(this RequestOptions options, string name, string? value)
    {
        var dict = options.Headers is null
            ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(options.Headers, StringComparer.OrdinalIgnoreCase);
        dict[name] = value;
        return options with { Headers = dict };
    }
}

/// <summary>
/// Per-request overrides: query string, custom headers, timeout, retry budget.
/// </summary>
/// <remarks>
/// Every field is optional; a <c>null</c> field means "use the client/global default".
/// Headers and query values may be <c>null</c> to skip that entry.
/// </remarks>
public sealed record RequestOptions
{
    /// <summary>Query-string parameters appended to the request URI.</summary>
    public IReadOnlyDictionary<string, string?>? Query { get; init; }

    /// <summary>Headers added to this request only (do not mutate the underlying <c>HttpClient</c>).</summary>
    public IReadOnlyDictionary<string, string?>? Headers { get; init; }

    /// <summary>Hard timeout for this request. Composes with the caller's <c>CancellationToken</c>.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Override <see cref="NetworkBlastOptions.DefaultRetryCount"/> for this call. <c>0</c> disables retry.</summary>
    public int? RetryCount { get; init; }

    /// <summary>Override <see cref="NetworkBlastOptions.DefaultRetryBaseDelay"/> for this call.</summary>
    public TimeSpan? RetryBaseDelay { get; init; }
}
