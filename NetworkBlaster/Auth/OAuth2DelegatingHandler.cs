using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkBlaster.Auth;

/// <summary>
/// HTTP message handler that attaches an OAuth2 bearer token (via
/// <see cref="OAuth2TokenProvider"/>) and retries once on a 401, after
/// invalidating the cached token, in case the token went stale mid-flight.
/// </summary>
internal sealed class OAuth2DelegatingHandler : DelegatingHandler
{
    private readonly OAuth2TokenProvider _provider;

    public OAuth2DelegatingHandler(OAuth2TokenProvider provider, HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await ApplyTokenAsync(request, cancellationToken).ConfigureAwait(false);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;

        // 401 → drop the cache and try once more with a freshly minted token.
        response.Dispose();
        await _provider.InvalidateAsync().ConfigureAwait(false);
        await ApplyTokenAsync(request, cancellationToken).ConfigureAwait(false);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyTokenAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await _provider.GetAccessTokenAsync(ct).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
