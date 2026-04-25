using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkBlaster.Auth;

/// <summary>
/// Fetches and caches an OAuth2 access token via the
/// <c>client_credentials</c> grant. Thread-safe; concurrent callers share a
/// single in-flight fetch instead of stampeding the token endpoint.
/// </summary>
/// <remarks>
/// Tokens are refreshed automatically when within <see cref="RefreshSkew"/>
/// of expiry, or on demand via <see cref="InvalidateAsync"/> after a 401.
/// </remarks>
public sealed class OAuth2TokenProvider
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;
    private readonly Uri _tokenEndpoint;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string? _scope;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    /// <summary>
    /// Builds a token provider. The <paramref name="httpClient"/> is used only for
    /// token-endpoint calls; pass <c>null</c> to let the provider create its own.
    /// </summary>
    public OAuth2TokenProvider(
        Uri tokenEndpoint,
        string clientId,
        string clientSecret,
        string? scope = null,
        HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(tokenEndpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(clientSecret);

        _http = httpClient ?? new HttpClient();
        _tokenEndpoint = tokenEndpoint;
        _clientId = clientId;
        _clientSecret = clientSecret;
        _scope = scope;
    }

    /// <summary>Returns a non-expired access token, fetching one if necessary.</summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (IsValid(_cachedToken, _expiresAt)) return _cachedToken!;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsValid(_cachedToken, _expiresAt)) return _cachedToken!;

            var (token, expiresAt) = await FetchAsync(cancellationToken).ConfigureAwait(false);
            _cachedToken = token;
            _expiresAt = expiresAt;
            return token;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Drops the cached token so the next call to <see cref="GetAccessTokenAsync"/>
    /// fetches a fresh one. Use after a 401 to recover from a stale (but cached) token.
    /// </summary>
    public Task InvalidateAsync()
    {
        _cachedToken = null;
        _expiresAt = DateTimeOffset.MinValue;
        return Task.CompletedTask;
    }

    private static bool IsValid(string? token, DateTimeOffset expiresAt)
        => !string.IsNullOrEmpty(token) && DateTimeOffset.UtcNow + RefreshSkew < expiresAt;

    private async Task<(string token, DateTimeOffset expiresAt)> FetchAsync(CancellationToken ct)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "client_credentials"),
            new("client_id", _clientId),
            new("client_secret", _clientSecret),
        };
        if (!string.IsNullOrWhiteSpace(_scope)) fields.Add(new("scope", _scope!));

        using var request = new HttpRequestMessage(HttpMethod.Post, _tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(fields),
        };

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException(
                $"NetworkBlaster.OAuth2: token endpoint returned {(int)response.StatusCode}. Body: {body}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var payload = await System.Text.Json.JsonSerializer
            .DeserializeAsync<TokenResponse>(stream, JsonOptions, ct)
            .ConfigureAwait(false)
            ?? throw new HttpRequestException("NetworkBlaster.OAuth2: token endpoint returned an empty body.");

        if (string.IsNullOrEmpty(payload.AccessToken))
            throw new HttpRequestException("NetworkBlaster.OAuth2: token endpoint response missing access_token.");

        var lifetime = payload.ExpiresIn > 0 ? TimeSpan.FromSeconds(payload.ExpiresIn) : TimeSpan.FromHours(1);
        return (payload.AccessToken, DateTimeOffset.UtcNow + lifetime);
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("token_type")]   public string? TokenType   { get; set; }
        [JsonPropertyName("expires_in")]   public int     ExpiresIn   { get; set; }
        [JsonPropertyName("scope")]        public string? Scope       { get; set; }
    }
}
