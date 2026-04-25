using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NetworkBlaster.Interfaces;

namespace NetworkBlaster;

/// <summary>
/// Default <see cref="INetClient"/> implementation: a thin <see cref="HttpClient"/>
/// wrapper that resolves base URL and auth from a <see cref="SecretResolver"/>
/// on first request and caches them for the lifetime of the instance.
/// </summary>
/// <remarks>
/// Resolver lookups (defaults shown):
/// <list type="bullet">
/// <item><c>(connectionName, "baseUrl")</c> — required.</item>
/// <item><c>(connectionName, "token")</c>   — optional; sent as <c>Authorization: Bearer &lt;value&gt;</c>.</item>
/// </list>
/// Override the keys via <see cref="NetworkBlasterOptions.BaseUrlKey"/> /
/// <see cref="NetworkBlasterOptions.TokenKey"/> when registering the service.
/// </remarks>
public sealed class NetClient : INetClient
{
    private readonly HttpClient _http;
    private readonly SecretResolver _resolver;
    private readonly string _baseUrlKey;
    private readonly string _tokenKey;
    private readonly SemaphoreSlim _hydrateLock = new(1, 1);
    private bool _hydrated;

    /// <inheritdoc/>
    public string ConnectionName { get; }

    /// <summary>
    /// Builds a client bound to <paramref name="connectionName"/>; secret values are resolved
    /// the first time a request is made.
    /// </summary>
    /// <param name="resolver">Delegate that returns secret values for (category, key) pairs.</param>
    /// <param name="connectionName">Name used as the resolver category for this connection's secrets.</param>
    /// <param name="httpClient">Optional pre-built <see cref="HttpClient"/>; a fresh instance is created when null.</param>
    /// <param name="baseUrlKey">Resolver key for the connection's base URL. Defaults to <c>baseUrl</c>.</param>
    /// <param name="tokenKey">Resolver key for the optional bearer token. Defaults to <c>token</c>.</param>
    public NetClient(
        SecretResolver resolver,
        string connectionName,
        HttpClient? httpClient = null,
        string baseUrlKey = "baseUrl",
        string tokenKey = "token")
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        _resolver = resolver;
        _http = httpClient ?? new HttpClient();
        _baseUrlKey = baseUrlKey;
        _tokenKey = tokenKey;
        ConnectionName = connectionName;
    }

    /// <summary>
    /// Script-friendly factory: an anonymous (no-auth) client pinned to <paramref name="baseUrl"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// // in a .csx / LINQPad / PowerShell script
    /// var api = NetClient.Anonymous("https://api.publicapis.org/");
    /// var body = await api.GetStringAsync("entries");
    /// </code>
    /// </example>
    public static NetClient Anonymous(string baseUrl, HttpClient? httpClient = null)
        => Inline(baseUrl, token: null, httpClient);

    /// <summary>
    /// Script-friendly factory: a client with a hard-coded bearer token, no resolver / no vault.
    /// </summary>
    /// <example>
    /// <code>
    /// var gh = NetClient.WithToken("https://api.github.com/", "ghp_xxx");
    /// var json = await gh.GetStringAsync("repos/octocat/hello-world");
    /// </code>
    /// </example>
    public static NetClient WithToken(string baseUrl, string token, HttpClient? httpClient = null)
        => Inline(baseUrl, token, httpClient);

    private static NetClient Inline(string baseUrl, string? token, HttpClient? httpClient)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        Task<string> Resolver(string category, string key, CancellationToken ct) => key switch
        {
            "baseUrl" => Task.FromResult(baseUrl),
            "token"   => Task.FromResult(token ?? string.Empty),
            _         => Task.FromResult(string.Empty),
        };
        return new NetClient(Resolver, "inline", httpClient);
    }

    /// <inheritdoc/>
    public Task<HttpResponseMessage> GetAsync(string path, CancellationToken cancellationToken = default)
        => SendAsync(new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

    /// <inheritdoc/>
    public Task<HttpResponseMessage> PostAsync(string path, HttpContent? content, CancellationToken cancellationToken = default)
        => SendAsync(new HttpRequestMessage(HttpMethod.Post, path) { Content = content }, cancellationToken);

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await EnsureHydratedAsync(cancellationToken).ConfigureAwait(false);

        if (request.RequestUri is { IsAbsoluteUri: false } && _http.BaseAddress is not null)
        {
            request.RequestUri = new Uri(_http.BaseAddress, request.RequestUri);
        }

        return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string> GetStringAsync(string path, CancellationToken cancellationToken = default)
    {
        using var response = await GetAsync(path, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<T?> GetJsonAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        using var response = await GetAsync(path, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<TResponse?> PostJsonAsync<TResponse>(string path, object? payload, CancellationToken cancellationToken = default)
    {
        var body = payload is null
            ? null
            : new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await PostAsync(path, body, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<TResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private async Task EnsureHydratedAsync(CancellationToken ct)
    {
        if (_hydrated) return;

        await _hydrateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_hydrated) return;

            var baseUrl = await _resolver(ConnectionName, _baseUrlKey, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException(
                    $"NetworkBlaster: connection '{ConnectionName}' is missing a '{_baseUrlKey}' value.");

            _http.BaseAddress = new Uri(baseUrl, UriKind.Absolute);

            string? token = null;
            try { token = await _resolver(ConnectionName, _tokenKey, ct).ConfigureAwait(false); }
            catch { /* token is optional; resolvers may throw on missing key */ }

            if (!string.IsNullOrWhiteSpace(token))
            {
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            _hydrated = true;
        }
        finally
        {
            _hydrateLock.Release();
        }
    }
}
