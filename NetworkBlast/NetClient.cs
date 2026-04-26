using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NetworkBlast.Interfaces;

namespace NetworkBlast;

/// <summary>
/// Default <see cref="INetClient"/> implementation: a thin <see cref="HttpClient"/>
/// wrapper that resolves base URL and auth from a
/// <c>Func&lt;category, key, ct, Task&lt;string&gt;&gt;</c> resolver on first
/// request and caches them for the lifetime of the instance.
/// </summary>
/// <remarks>
/// Resolver lookups (defaults shown):
/// <list type="bullet">
/// <item><c>(connectionName, "baseUrl")</c> — required.</item>
/// <item><c>(connectionName, "token")</c>   — optional; sent as <c>Authorization: Bearer &lt;value&gt;</c>.</item>
/// </list>
/// Override the keys via <see cref="NetworkBlastOptions.BaseUrlKey"/> /
/// <see cref="NetworkBlastOptions.TokenKey"/> when registering the service.
/// </remarks>
public sealed class NetClient : INetClient
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly Func<string, string, CancellationToken, Task<string>> _resolver;
    private readonly string _baseUrlKey;
    private readonly string _tokenKey;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly int _defaultRetryCount;
    private readonly TimeSpan _defaultRetryBaseDelay;
    private readonly SemaphoreSlim _hydrateLock = new(1, 1);
    private readonly Dictionary<string, string?> _defaultQuery = new(StringComparer.OrdinalIgnoreCase);
    private bool _hydrated;

    /// <inheritdoc/>
    public string ConnectionName { get; }

    /// <summary>
    /// Builds a client bound to <paramref name="connectionName"/>; secret values are resolved
    /// the first time a request is made.
    /// </summary>
    public NetClient(
        Func<string, string, CancellationToken, Task<string>> resolver,
        string connectionName,
        HttpClient? httpClient = null,
        string baseUrlKey = "baseUrl",
        string tokenKey = "token",
        JsonSerializerOptions? jsonOptions = null,
        int defaultRetryCount = 0,
        TimeSpan? defaultRetryBaseDelay = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);
        if (defaultRetryCount < 0) throw new ArgumentOutOfRangeException(nameof(defaultRetryCount));

        _resolver = resolver;
        _http = httpClient ?? new HttpClient();
        _baseUrlKey = baseUrlKey;
        _tokenKey = tokenKey;
        _jsonOptions = jsonOptions ?? DefaultJsonOptions;
        _defaultRetryCount = defaultRetryCount;
        _defaultRetryBaseDelay = defaultRetryBaseDelay ?? TimeSpan.FromMilliseconds(200);
        ConnectionName = connectionName;
    }

    /// <summary>Script-friendly factory: an anonymous (no-auth) client pinned to <paramref name="baseUrl"/>.</summary>
    public static NetClient Anonymous(
        string baseUrl,
        HttpClient? httpClient = null,
        JsonSerializerOptions? jsonOptions = null)
        => Inline(baseUrl, token: null, httpClient, jsonOptions);

    /// <summary>Script-friendly factory: a client with a hard-coded bearer token, no resolver / no vault.</summary>
    public static NetClient WithToken(
        string baseUrl,
        string token,
        HttpClient? httpClient = null,
        JsonSerializerOptions? jsonOptions = null)
        => Inline(baseUrl, token, httpClient, jsonOptions);

    /// <summary>
    /// Script-friendly factory: a client that sends a fixed API-key header
    /// (e.g. <c>X-API-Key: ...</c>) on every request.
    /// </summary>
    public static NetClient WithApiKey(
        string baseUrl,
        string headerName,
        string apiKey,
        HttpClient? httpClient = null,
        JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        var http = httpClient ?? new HttpClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation(headerName, apiKey);
        return Inline(baseUrl, token: null, http, jsonOptions);
    }

    /// <summary>
    /// Script-friendly factory: a client that sends HTTP Basic auth
    /// (<c>Authorization: Basic base64(user:pass)</c>) on every request.
    /// </summary>
    public static NetClient WithBasicAuth(
        string baseUrl,
        string username,
        string password,
        HttpClient? httpClient = null,
        JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);

        var http = httpClient ?? new HttpClient();
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        return Inline(baseUrl, token: null, http, jsonOptions);
    }

    /// <summary>
    /// Adds a default header that will be sent on every request from this client.
    /// Returns the same instance for chaining.
    /// </summary>
    /// <example>
    /// <code>
    /// var c = NetClient.Anonymous("https://api.example.com/")
    ///     .WithDefaultHeader("User-Agent", "my-script/1.0");
    /// </code>
    /// </example>
    public NetClient WithDefaultHeader(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _http.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
        return this;
    }

    /// <summary>
    /// Adds a default query-string parameter sent on every request. Per-request
    /// values from <see cref="RequestOptions.Query"/> override the default on key collision.
    /// </summary>
    public NetClient WithDefaultQuery(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _defaultQuery[name] = value;
        return this;
    }

    /// <summary>
    /// Script-friendly factory: a client that appends a fixed API-key parameter
    /// (e.g. <c>?api_key=...</c>) to every request URL.
    /// </summary>
    /// <example>
    /// <code>
    /// var w = NetClient.WithApiKeyQuery("https://api.weather.example/", "appid", "abc123");
    /// var json = await w.GetStringAsync("data/2.5/weather?q=Paris");
    /// </code>
    /// </example>
    public static NetClient WithApiKeyQuery(
        string baseUrl,
        string paramName,
        string apiKey,
        HttpClient? httpClient = null,
        JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paramName);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        var client = Inline(baseUrl, token: null, httpClient, jsonOptions);
        client._defaultQuery[paramName] = apiKey;
        return client;
    }

    /// <summary>
    /// Script-friendly factory: a client that authenticates with the current Windows
    /// user's credentials (NTLM / Negotiate / Kerberos). Typically used inside corporate
    /// intranets.
    /// </summary>
    public static NetClient WithWindowsAuth(
        string baseUrl,
        JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        var handler = new HttpClientHandler { UseDefaultCredentials = true };
        return Inline(baseUrl, token: null, new HttpClient(handler), jsonOptions);
    }

    /// <summary>
    /// Script-friendly factory: a client that authenticates using the supplied
    /// Windows credentials (NTLM / Negotiate / Kerberos).
    /// </summary>
    public static NetClient WithWindowsAuth(
        string baseUrl,
        System.Net.NetworkCredential credentials,
        JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(credentials);
        var handler = new HttpClientHandler { Credentials = credentials, UseDefaultCredentials = false };
        return Inline(baseUrl, token: null, new HttpClient(handler), jsonOptions);
    }

    /// <summary>
    /// Script-friendly factory: a client with an attached cookie jar. Cookies set by
    /// the server (via <c>Set-Cookie</c>) are automatically replayed on follow-up
    /// requests. Pass an existing <see cref="CookieContainer"/> to share state across clients.
    /// </summary>
    public static NetClient WithCookieJar(
        string baseUrl,
        System.Net.CookieContainer? cookies = null,
        JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = cookies ?? new System.Net.CookieContainer(),
        };
        return Inline(baseUrl, token: null, new HttpClient(handler), jsonOptions);
    }

    /// <summary>
    /// Script-friendly factory: a client that authenticates with OAuth2
    /// <c>client_credentials</c>. Tokens are fetched from <paramref name="tokenEndpoint"/>,
    /// cached, refreshed before expiry, and re-fetched once on a 401 response.
    /// </summary>
    /// <example>
    /// <code>
    /// var graph = NetClient.WithOAuth2ClientCredentials(
    ///     baseUrl:       "https://graph.microsoft.com/v1.0/",
    ///     tokenEndpoint: "https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token",
    ///     clientId:      "...",
    ///     clientSecret:  "...",
    ///     scope:         "https://graph.microsoft.com/.default");
    ///
    /// var me = await graph.GetJsonAsync&lt;User&gt;("me");
    /// </code>
    /// </example>
    public static NetClient WithOAuth2ClientCredentials(
        string baseUrl,
        string tokenEndpoint,
        string clientId,
        string clientSecret,
        string? scope = null,
        HttpClient? tokenClient = null,
        JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenEndpoint);

        var provider = new Auth.OAuth2TokenProvider(
            new Uri(tokenEndpoint, UriKind.Absolute),
            clientId,
            clientSecret,
            scope,
            tokenClient);

        var handler = new Auth.OAuth2DelegatingHandler(provider, new HttpClientHandler());
        return Inline(baseUrl, token: null, new HttpClient(handler), jsonOptions);
    }

    /// <summary>
    /// Variant of <see cref="WithOAuth2ClientCredentials(string, string, string, string, string?, HttpClient?, JsonSerializerOptions?)"/>
    /// that lets you supply a pre-built <see cref="Auth.OAuth2TokenProvider"/>, e.g. to share a token cache across clients.
    /// </summary>
    public static NetClient WithOAuth2(
        string baseUrl,
        Auth.OAuth2TokenProvider provider,
        JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(provider);
        var handler = new Auth.OAuth2DelegatingHandler(provider, new HttpClientHandler());
        return Inline(baseUrl, token: null, new HttpClient(handler), jsonOptions);
    }

    private static NetClient Inline(string baseUrl, string? token, HttpClient? httpClient, JsonSerializerOptions? jsonOptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        Task<string> Resolver(string category, string key, CancellationToken ct) => key switch
        {
            "baseUrl" => Task.FromResult(baseUrl),
            "token"   => Task.FromResult(token ?? string.Empty),
            _         => Task.FromResult(string.Empty),
        };
        return new NetClient(Resolver, "inline", httpClient, jsonOptions: jsonOptions);
    }

    /// <inheritdoc/>
    public Task<HttpResponseMessage> GetAsync(string path, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => SendCoreAsync(HttpMethod.Get, path, content: null, options, cancellationToken);

    /// <inheritdoc/>
    public Task<HttpResponseMessage> PostAsync(string path, HttpContent? content, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => SendCoreAsync(HttpMethod.Post, path, content, options, cancellationToken);

    /// <inheritdoc/>
    public Task<HttpResponseMessage> PutAsync(string path, HttpContent? content, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => SendCoreAsync(HttpMethod.Put, path, content, options, cancellationToken);

    /// <inheritdoc/>
    public Task<HttpResponseMessage> PatchAsync(string path, HttpContent? content, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => SendCoreAsync(HttpMethod.Patch, path, content, options, cancellationToken);

    /// <inheritdoc/>
    public Task<HttpResponseMessage> DeleteAsync(string path, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => SendCoreAsync(HttpMethod.Delete, path, content: null, options, cancellationToken);

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await EnsureHydratedAsync(cancellationToken).ConfigureAwait(false);

        ApplyBaseAddress(request);
        ApplyQuery(request, MergeQuery(options?.Query));
        ApplyHeaders(request, options?.Headers);

        return await SendWithPolicyAsync(_ => CloneRequest(request), options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string> GetStringAsync(string path, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var response = await GetAsync(path, options, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<T?> GetJsonAsync<T>(string path, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => ReadJsonAsync<T>(GetAsync(path, options, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<TResponse?> PostJsonAsync<TResponse>(string path, object? payload, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => ReadJsonAsync<TResponse>(PostAsync(path, BuildJsonContent(payload), options, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<TResponse?> PutJsonAsync<TResponse>(string path, object? payload, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => ReadJsonAsync<TResponse>(PutAsync(path, BuildJsonContent(payload), options, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<TResponse?> PatchJsonAsync<TResponse>(string path, object? payload, RequestOptions? options = null, CancellationToken cancellationToken = default)
        => ReadJsonAsync<TResponse>(PatchAsync(path, BuildJsonContent(payload), options, cancellationToken), cancellationToken);

    private HttpContent? BuildJsonContent(object? payload)
        => payload is null
            ? null
            : new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");

    private async Task<T?> ReadJsonAsync<T>(Task<HttpResponseMessage> responseTask, CancellationToken ct)
    {
        using var response = await responseTask.ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        if (stream.CanSeek && stream.Length == 0) return default;
        return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(
        HttpMethod method, string path, HttpContent? content, RequestOptions? options, CancellationToken cancellationToken)
    {
        await EnsureHydratedAsync(cancellationToken).ConfigureAwait(false);

        return await SendWithPolicyAsync(_ =>
        {
            var request = new HttpRequestMessage(method, path);
            if (content is not null) request.Content = content;
            ApplyBaseAddress(request);
            ApplyQuery(request, MergeQuery(options?.Query));
            ApplyHeaders(request, options?.Headers);
            return request;
        }, options, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendWithPolicyAsync(
        Func<int, HttpRequestMessage> requestFactory,
        RequestOptions? options,
        CancellationToken cancellationToken)
    {
        var retries = options?.RetryCount ?? _defaultRetryCount;
        var baseDelay = options?.RetryBaseDelay ?? _defaultRetryBaseDelay;
        var timeout = options?.Timeout;

        HttpResponseMessage? response = null;
        Exception? lastError = null;

        for (var attempt = 0; attempt <= retries; attempt++)
        {
            response?.Dispose();
            response = null;
            lastError = null;

            using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeout is { } t) requestCts.CancelAfter(t);

            var request = requestFactory(attempt);
            try
            {
                response = await _http.SendAsync(request, requestCts.Token).ConfigureAwait(false);
                if (!IsTransient(response.StatusCode)) return response;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException)
            {
                lastError = ex;
            }

            if (attempt == retries) break;

            var delay = ComputeBackoff(baseDelay, attempt, response);
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
        }

        if (response is not null) return response;
        throw lastError ?? new HttpRequestException($"NetworkBlast: request to '{ConnectionName}' failed after {retries + 1} attempt(s).");
    }

    private static bool IsTransient(HttpStatusCode status)
        => (int)status >= 500
           || status == HttpStatusCode.RequestTimeout    // 408
           || status == HttpStatusCode.TooManyRequests;  // 429

    private static TimeSpan ComputeBackoff(TimeSpan baseDelay, int attempt, HttpResponseMessage? response)
    {
        if (response?.Headers.RetryAfter is { } retryAfter)
        {
            if (retryAfter.Delta is { } d && d > TimeSpan.Zero) return d;
            if (retryAfter.Date is { } when_)
            {
                var until = when_ - DateTimeOffset.UtcNow;
                if (until > TimeSpan.Zero) return until;
            }
        }

        var exponential = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
        var jitterMs = Random.Shared.Next(0, 50);
        return exponential + TimeSpan.FromMilliseconds(jitterMs);
    }

    private void ApplyBaseAddress(HttpRequestMessage request)
    {
        if (request.RequestUri is { IsAbsoluteUri: false } && _http.BaseAddress is not null)
        {
            request.RequestUri = new Uri(_http.BaseAddress, request.RequestUri);
        }
    }

    private IReadOnlyDictionary<string, string?>? MergeQuery(IReadOnlyDictionary<string, string?>? perRequest)
    {
        if (_defaultQuery.Count == 0) return perRequest;
        if (perRequest is null || perRequest.Count == 0) return _defaultQuery;

        var merged = new Dictionary<string, string?>(_defaultQuery, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in perRequest) merged[k] = v;
        return merged;
    }

    private static void ApplyQuery(HttpRequestMessage request, IReadOnlyDictionary<string, string?>? query)
    {
        if (query is null || query.Count == 0 || request.RequestUri is null) return;

        var uri = request.RequestUri;
        var builder = uri.IsAbsoluteUri
            ? new UriBuilder(uri)
            : new UriBuilder(new Uri("http://placeholder/" + uri.OriginalString));

        var separator = string.IsNullOrEmpty(builder.Query) ? "" : builder.Query.TrimStart('?') + "&";
        var sb = new StringBuilder(separator);
        var first = sb.Length == 0;
        foreach (var kvp in query)
        {
            if (kvp.Value is null) continue;
            if (!first) sb.Append('&');
            sb.Append(Uri.EscapeDataString(kvp.Key)).Append('=').Append(Uri.EscapeDataString(kvp.Value));
            first = false;
        }
        builder.Query = sb.ToString();

        request.RequestUri = uri.IsAbsoluteUri
            ? builder.Uri
            : new Uri(builder.Uri.PathAndQuery + builder.Uri.Fragment, UriKind.Relative);
    }

    private static void ApplyHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string?>? headers)
    {
        if (headers is null || headers.Count == 0) return;

        foreach (var (key, value) in headers)
        {
            if (value is null) continue;
            if (!request.Headers.TryAddWithoutValidation(key, value) && request.Content is not null)
            {
                request.Content.Headers.TryAddWithoutValidation(key, value);
            }
        }
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage source)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri) { Version = source.Version };
        foreach (var header in source.Headers) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        clone.Content = source.Content;
        return clone;
    }

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
                    $"NetworkBlast: connection '{ConnectionName}' is missing a '{_baseUrlKey}' value.");

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
