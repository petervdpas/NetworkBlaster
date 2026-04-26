using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NetworkBlast.Interfaces;

namespace NetworkBlast.OData;

/// <summary>
/// Typed LINQ-flavored OData query. Build it via <see cref="ODataExtensions.OData{T}(INetClient, string)"/>
/// and chain <c>Where</c> / <see cref="OrderBy(Expression{Func{T, object?}})"/> / etc.
/// </summary>
/// <remarks>
/// Implements <see cref="IAsyncEnumerable{T}"/>; iterating auto-pages via <c>@odata.nextLink</c>.
/// Multiple <c>Where</c> calls are <c>and</c>-merged. Multiple <c>OrderBy</c> calls compose
/// like <c>ThenBy</c> in standard LINQ.
/// </remarks>
public sealed class ODataRequest<T> : IAsyncEnumerable<T>
{
    private readonly INetClient _client;
    private readonly string _path;
    private readonly ODataQuery _query;

    internal ODataRequest(INetClient client, string path, ODataQuery query)
    {
        _client = client;
        _path = path;
        _query = query;
    }

    private ODataRequest<T> With(ODataQuery next) => new(_client, _path, next);

    // ---------- chainable shapers ----------

    /// <summary>And-merges a typed LINQ predicate into <c>$filter</c>.</summary>
    public ODataRequest<T> Where(Expression<Func<T, bool>> predicate)
        => With(_query.WithFilter(predicate));

    /// <summary>And-merges an <see cref="ODataFilter"/> into <c>$filter</c>.</summary>
    public ODataRequest<T> Where(ODataFilter filter)
        => With(_query.WithFilter(filter));

    /// <summary>And-merges a raw OData filter expression string into <c>$filter</c>.</summary>
    public ODataRequest<T> Where(string rawExpression)
        => With(_query.WithFilter(rawExpression));

    /// <summary>Sets the primary sort. Subsequent <see cref="ThenBy"/> calls are tiebreakers.</summary>
    public ODataRequest<T> OrderBy(Expression<Func<T, object?>> selector)
        => With(_query.OrderBy(selector));

    /// <summary>Sets the primary sort, descending.</summary>
    public ODataRequest<T> OrderByDescending(Expression<Func<T, object?>> selector)
        => With(_query.OrderByDescending(selector));

    /// <summary>Adds a tiebreaker sort. Functionally identical to <see cref="OrderBy"/>; named for LINQ familiarity.</summary>
    public ODataRequest<T> ThenBy(Expression<Func<T, object?>> selector)
        => With(_query.OrderBy(selector));

    /// <summary>Adds a tiebreaker sort, descending.</summary>
    public ODataRequest<T> ThenByDescending(Expression<Func<T, object?>> selector)
        => With(_query.OrderByDescending(selector));

    /// <summary>Sets <c>$select</c> via member-access expressions on <typeparamref name="T"/>.</summary>
    public ODataRequest<T> Select(params Expression<Func<T, object?>>[] fields)
        => With(_query.Select(fields));

    /// <summary>Sets <c>$expand</c> via member-access expressions on <typeparamref name="T"/>.</summary>
    public ODataRequest<T> Expand(params Expression<Func<T, object?>>[] navigations)
        => With(_query.Expand(navigations));

    /// <summary>Sets <c>$top</c>.</summary>
    public ODataRequest<T> Top(int count)    => With(_query.Top(count));
    /// <summary>Sets <c>$skip</c>.</summary>
    public ODataRequest<T> Skip(int count)   => With(_query.Skip(count));
    /// <summary>Sets <c>$search</c>.</summary>
    public ODataRequest<T> Search(string text) => With(_query.Search(text));
    /// <summary>Toggles <c>$count=true</c> on the request.</summary>
    public ODataRequest<T> WithCount(bool include = true) => With(_query.Count(include));

    // ---------- terminals ----------

    /// <summary>Runs a single page request and returns the raw <see cref="ODataPage{T}"/>.</summary>
    public Task<ODataPage<T>> FirstPageAsync(CancellationToken cancellationToken = default)
        => _client.GetODataAsync<T>(_path, _query, options: null, cancellationToken);

    /// <summary>Materialises every page into a list (auto-pages via <c>@odata.nextLink</c>).</summary>
    public async Task<IReadOnlyList<T>> ToListAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<T>();
        await foreach (var item in this.WithCancellation(cancellationToken).ConfigureAwait(false))
            list.Add(item);
        return list;
    }

    /// <summary>Returns the first matching entity, or <c>default</c> if none. Uses <c>$top=1</c>.</summary>
    public async Task<T?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        var page = await With(_query.Top(1)).FirstPageAsync(cancellationToken).ConfigureAwait(false);
        return page.Value.Count == 0 ? default : page.Value[0];
    }

    /// <summary>
    /// Returns the single matching entity. Throws if more than one is returned.
    /// Uses <c>$top=2</c> to detect duplicates.
    /// </summary>
    public async Task<T?> SingleOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        var page = await With(_query.Top(2)).FirstPageAsync(cancellationToken).ConfigureAwait(false);
        return page.Value.Count switch
        {
            0 => default,
            1 => page.Value[0],
            _ => throw new InvalidOperationException("ODataRequest.SingleOrDefaultAsync: more than one entity matched."),
        };
    }

    /// <summary>Asks the server for <c>@odata.count</c> via <c>$count=true&amp;$top=0</c>.</summary>
    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        var page = await With(_query.Count().Top(0)).FirstPageAsync(cancellationToken).ConfigureAwait(false);
        return page.Count
               ?? throw new InvalidOperationException("ODataRequest.CountAsync: server did not return @odata.count.");
    }

    // ---------- IAsyncEnumerable ----------

    /// <summary>Returns an enumerator that yields entities across every page.</summary>
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => Stream(cancellationToken).GetAsyncEnumerator(cancellationToken);

    private async IAsyncEnumerable<T> Stream([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in _client.QueryODataAsync<T>(_path, _query, options: null, cancellationToken).ConfigureAwait(false))
            yield return item;
    }

    /// <summary>Inspect the underlying <see cref="ODataQuery"/> (mostly for tests / diagnostics).</summary>
    public ODataQuery Query => _query;

    /// <summary>Inspect the relative path this request is bound to.</summary>
    public string Path => _path;
}
