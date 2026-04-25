using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NetworkBlaster.Interfaces;

namespace NetworkBlaster.OData;

/// <summary>
/// OData v4 extensions on <see cref="INetClient"/>: collection page fetch, single entity fetch,
/// and an auto-paging <see cref="IAsyncEnumerable{T}"/> stream.
/// </summary>
public static class ODataExtensions
{
    /// <summary>Entry point for the typed LINQ-flavored OData query.</summary>
    public static ODataRequest<T> OData<T>(this INetClient client, string path)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new ODataRequest<T>(client, path, ODataQuery.Empty);
    }

    /// <summary>Fetches one page of an OData collection at <paramref name="path"/>.</summary>
    public static async Task<ODataPage<T>> GetODataAsync<T>(
        this INetClient client,
        string path,
        ODataQuery? query = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var merged = MergeQuery(options, query);
        using var response = await client.GetAsync(path, merged, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var dto = await JsonSerializer.DeserializeAsync<ODataResponseDto<T>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                  ?? new ODataResponseDto<T>();
        return new ODataPage<T>
        {
            Value = (IReadOnlyList<T>?)dto.Value ?? Array.Empty<T>(),
            Count = dto.Count,
            NextLink = dto.NextLink,
            Context = dto.Context,
        };
    }

    /// <summary>Fetches a single OData entity at <paramref name="path"/>.</summary>
    public static Task<T?> GetODataItemAsync<T>(
        this INetClient client,
        string path,
        ODataQuery? query = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var merged = MergeQuery(options, query);
        return client.GetJsonAsync<T>(path, merged, cancellationToken);
    }

    /// <summary>
    /// Streams every entity across every page, automatically following <c>@odata.nextLink</c>.
    /// Cancellation is honored between pages and during in-flight requests.
    /// </summary>
    public static async IAsyncEnumerable<T> QueryODataAsync<T>(
        this INetClient client,
        string path,
        ODataQuery? query = null,
        RequestOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var page = await client.GetODataAsync<T>(path, query, options, cancellationToken).ConfigureAwait(false);
        foreach (var item in page.Value) yield return item;

        var nextLink = page.NextLink;
        while (!string.IsNullOrEmpty(nextLink))
        {
            cancellationToken.ThrowIfCancellationRequested();
            page = await FetchAbsoluteAsync<T>(client, nextLink, cancellationToken).ConfigureAwait(false);
            foreach (var item in page.Value) yield return item;
            nextLink = page.NextLink;
        }
    }

    internal static async Task<ODataPage<T>> FetchAbsoluteAsync<T>(INetClient client, string absoluteUrl, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(absoluteUrl, UriKind.Absolute));
        using var response = await client.SendAsync(request, options: null, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var dto = await JsonSerializer.DeserializeAsync<ODataResponseDto<T>>(stream, JsonOptions, ct).ConfigureAwait(false)
                  ?? new ODataResponseDto<T>();
        return new ODataPage<T>
        {
            Value = (IReadOnlyList<T>?)dto.Value ?? Array.Empty<T>(),
            Count = dto.Count,
            NextLink = dto.NextLink,
            Context = dto.Context,
        };
    }

    private static RequestOptions? MergeQuery(RequestOptions? caller, ODataQuery? query)
    {
        if (query is null || query == ODataQuery.Empty) return caller;
        var built = query.Build();
        if (caller is null) return new RequestOptions { Query = built };

        var merged = caller.Query is null
            ? new Dictionary<string, string?>(built, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(caller.Query, StringComparer.OrdinalIgnoreCase);
        if (caller.Query is not null)
            foreach (var (k, v) in built) merged[k] = v;
        return caller with { Query = merged };
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class ODataResponseDto<T>
    {
        [JsonPropertyName("value")]
        public List<T>? Value { get; set; }

        [JsonPropertyName("@odata.count")]
        public long? Count { get; set; }

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }

        [JsonPropertyName("@odata.context")]
        public string? Context { get; set; }
    }
}
