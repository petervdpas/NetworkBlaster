using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkBlaster.Interfaces;

#pragma warning disable CA1716 // Reserved language keyword 'Get'/'Post' — script-friendly names take priority.

/// <summary>
/// Programmable HTTP client bound to a single named connection.
/// </summary>
/// <remarks>
/// A connection is a logical group of secrets (base URL, auth token, headers)
/// resolved lazily through a <see cref="SecretResolver"/> on first use and cached
/// for the lifetime of the client.
/// </remarks>
public interface INetClient
{
    /// <summary>Connection name this client is bound to (used as the resolver category).</summary>
    string ConnectionName { get; }

    /// <summary>Issues a GET against <paramref name="path"/> relative to the connection's base URL.</summary>
    Task<HttpResponseMessage> GetAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Issues a POST with the supplied content against <paramref name="path"/>.</summary>
    Task<HttpResponseMessage> PostAsync(string path, HttpContent? content, CancellationToken cancellationToken = default);

    /// <summary>Issues an arbitrary <see cref="HttpRequestMessage"/> with auth + base address applied.</summary>
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);

    /// <summary>Convenience: GET <paramref name="path"/> and return the response body as a string.</summary>
    Task<string> GetStringAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Convenience: GET <paramref name="path"/> and deserialize the JSON body to <typeparamref name="T"/>.</summary>
    Task<T?> GetJsonAsync<T>(string path, CancellationToken cancellationToken = default);

    /// <summary>Convenience: POST <paramref name="payload"/> as JSON and deserialize the response to <typeparamref name="TResponse"/>.</summary>
    Task<TResponse?> PostJsonAsync<TResponse>(string path, object? payload, CancellationToken cancellationToken = default);
}

#pragma warning restore CA1716
