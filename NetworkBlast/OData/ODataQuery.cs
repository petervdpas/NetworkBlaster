using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace NetworkBlast.OData;

/// <summary>
/// Fluent builder for OData v4 query options (<c>$filter</c>, <c>$select</c>, <c>$orderby</c>,
/// <c>$top</c>, <c>$skip</c>, <c>$expand</c>, <c>$search</c>, <c>$count</c>). Compose freely;
/// methods return new instances (no mutation).
/// </summary>
/// <example>
/// <code>
/// var q = ODataQuery
///     .Filter(ODataFilter.For&lt;Customer&gt;(c =&gt; c.Status == "Active"))
///     .Select("Id", "Name")
///     .OrderBy("Name")
///     .Top(50)
///     .Count();
/// </code>
/// </example>
public sealed record ODataQuery
{
    private readonly string? _filter;
    private readonly IReadOnlyList<string>? _select;
    private readonly IReadOnlyList<string>? _expand;
    private readonly IReadOnlyList<OrderByClause>? _orderBy;
    private readonly int? _top;
    private readonly int? _skip;
    private readonly string? _search;
    private readonly bool _count;

    private ODataQuery(
        string? filter,
        IReadOnlyList<string>? select,
        IReadOnlyList<string>? expand,
        IReadOnlyList<OrderByClause>? orderBy,
        int? top,
        int? skip,
        string? search,
        bool count)
    {
        _filter = filter;
        _select = select;
        _expand = expand;
        _orderBy = orderBy;
        _top = top;
        _skip = skip;
        _search = search;
        _count = count;
    }

    /// <summary>Empty query — produces no parameters.</summary>
    public static ODataQuery Empty { get; } = new(null, null, null, null, null, null, null, false);

    // ---------- $filter entry-points ----------

    /// <summary>Starts a query whose <c>$filter</c> is the given raw expression.</summary>
    public static ODataQuery Filter(string expression)        => Empty.WithFilter(expression);

    /// <summary>Starts a query whose <c>$filter</c> is the given typed filter.</summary>
    public static ODataQuery Filter(ODataFilter filter)       => Empty.WithFilter(filter);

    /// <summary>Starts a query whose <c>$filter</c> is translated from the given LINQ predicate.</summary>
    public static ODataQuery Filter<T>(Expression<Func<T, bool>> predicate) => Empty.WithFilter(ODataFilter.For(predicate));

    /// <summary>And-merges <paramref name="expression"/> with any existing <c>$filter</c>.</summary>
    public ODataQuery WithFilter(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        return Combine(expression);
    }

    /// <summary>And-merges <paramref name="filter"/> with any existing <c>$filter</c>.</summary>
    public ODataQuery WithFilter(ODataFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Combine(filter.Render());
    }

    /// <summary>And-merges the LINQ-translated predicate with any existing <c>$filter</c>.</summary>
    public ODataQuery WithFilter<T>(Expression<Func<T, bool>> predicate) => WithFilter(ODataFilter.For(predicate));

    private ODataQuery Combine(string expression)
    {
        var merged = string.IsNullOrEmpty(_filter)
            ? expression
            : $"({_filter}) and ({expression})";
        return new(merged, _select, _expand, _orderBy, _top, _skip, _search, _count);
    }

    // ---------- $select ----------

    /// <summary>Adds field names to <c>$select</c>. Append-only; pass multiple per call or chain calls.</summary>
    public ODataQuery Select(params string[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Length == 0) return this;
        var combined = (_select ?? Array.Empty<string>()).Concat(fields).ToArray();
        return new(_filter, combined, _expand, _orderBy, _top, _skip, _search, _count);
    }

    /// <summary>Typed variant of <see cref="Select(string[])"/>; the fields are member-access expressions on <typeparamref name="T"/>.</summary>
    public ODataQuery Select<T>(params Expression<Func<T, object?>>[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return Select(fields.Select(ODataFilter.Path).ToArray());
    }

    // ---------- $expand ----------

    /// <summary>Adds navigation properties to <c>$expand</c>. Append-only.</summary>
    public ODataQuery Expand(params string[] navigations)
    {
        ArgumentNullException.ThrowIfNull(navigations);
        if (navigations.Length == 0) return this;
        var combined = (_expand ?? Array.Empty<string>()).Concat(navigations).ToArray();
        return new(_filter, _select, combined, _orderBy, _top, _skip, _search, _count);
    }

    /// <summary>Typed variant of <see cref="Expand(string[])"/>; navigations are member-access expressions on <typeparamref name="T"/>.</summary>
    public ODataQuery Expand<T>(params Expression<Func<T, object?>>[] navigations)
    {
        ArgumentNullException.ThrowIfNull(navigations);
        return Expand(navigations.Select(ODataFilter.Path).ToArray());
    }

    // ---------- $orderby ----------

    /// <summary>
    /// Appends an <c>$orderby</c> clause. Multiple calls compose like <c>ThenBy</c>;
    /// the first call is the primary sort, subsequent calls are tiebreakers.
    /// </summary>
    public ODataQuery OrderBy(string field, bool descending = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        var clauses = (_orderBy ?? Array.Empty<OrderByClause>())
            .Append(new OrderByClause(field, descending))
            .ToArray();
        return new(_filter, _select, _expand, clauses, _top, _skip, _search, _count);
    }

    /// <summary>Typed variant of <see cref="OrderBy(string, bool)"/>.</summary>
    public ODataQuery OrderBy<T>(Expression<Func<T, object?>> selector, bool descending = false)
        => OrderBy(ODataFilter.Path(selector), descending);

    /// <summary>Convenience: <c>OrderBy(field, descending: true)</c>.</summary>
    public ODataQuery OrderByDescending(string field) => OrderBy(field, descending: true);

    /// <summary>Typed variant of <see cref="OrderByDescending(string)"/>.</summary>
    public ODataQuery OrderByDescending<T>(Expression<Func<T, object?>> selector) => OrderBy(selector, descending: true);

    // ---------- $top / $skip / $search / $count ----------

    /// <summary>Sets <c>$top</c>. Negative values throw.</summary>
    public ODataQuery Top(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        return new(_filter, _select, _expand, _orderBy, count, _skip, _search, _count);
    }

    /// <summary>Sets <c>$skip</c>. Negative values throw.</summary>
    public ODataQuery Skip(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        return new(_filter, _select, _expand, _orderBy, _top, count, _search, _count);
    }

    /// <summary>Sets <c>$search</c> (server-defined free-text search; not all OData services support it).</summary>
    public ODataQuery Search(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new(_filter, _select, _expand, _orderBy, _top, _skip, text, _count);
    }

    /// <summary>
    /// Toggles <c>$count=true</c> so the server returns <c>@odata.count</c> alongside the page.
    /// Pass <c>false</c> to remove the option.
    /// </summary>
    public ODataQuery Count(bool include = true)
        => new(_filter, _select, _expand, _orderBy, _top, _skip, _search, include);

    // ---------- materialise ----------

    /// <summary>Produces a query-string fragment dictionary suitable for <see cref="RequestOptions.Query"/>.</summary>
    public IReadOnlyDictionary<string, string?> Build()
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(_filter)) dict["$filter"] = _filter;
        if (_select is { Count: > 0 }) dict["$select"] = string.Join(",", _select);
        if (_expand is { Count: > 0 }) dict["$expand"] = string.Join(",", _expand);
        if (_orderBy is { Count: > 0 })
        {
            var sb = new StringBuilder();
            for (var i = 0; i < _orderBy.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(_orderBy[i].Field);
                if (_orderBy[i].Descending) sb.Append(" desc");
            }
            dict["$orderby"] = sb.ToString();
        }
        if (_top is { } t) dict["$top"] = t.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (_skip is { } s) dict["$skip"] = s.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(_search)) dict["$search"] = _search;
        if (_count) dict["$count"] = "true";
        return dict;
    }

    /// <summary>Returns a <see cref="RequestOptions"/> carrying just this query.</summary>
    public RequestOptions ToRequestOptions() => new() { Query = Build() };

    private sealed record OrderByClause(string Field, bool Descending);
}
