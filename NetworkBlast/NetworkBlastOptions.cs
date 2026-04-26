using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NetworkBlast.Interfaces;

namespace NetworkBlast;

/// <summary>
/// Options used to register a <see cref="INetClient"/> with the DI container, and
/// the source of defaults for retry budget / JSON serialization on every request.
/// </summary>
/// <remarks>
/// NetworkBlast never reads the vault directly. Callers wire a resolver
/// delegate of shape <c>Func&lt;category, key, ct, Task&lt;string&gt;&gt;</c>
/// (typically <c>Secrets.Resolver</c> from TaskBlaster / SecretBlast) and the
/// client lazily pulls values at request time.
/// </remarks>
public class NetworkBlastOptions
{
    /// <summary>Delegate used to resolve named-connection secrets at runtime, shape <c>(category, key, ct) =&gt; Task&lt;string&gt;</c>.</summary>
    public Func<string, string, CancellationToken, Task<string>>? Resolver { get; set; }

    /// <summary>Logical connection name; used as the secret category.</summary>
    public string ConnectionName { get; set; } = string.Empty;

    /// <summary>Resolver key holding the connection's base URL. Defaults to <c>baseUrl</c>.</summary>
    public string BaseUrlKey { get; set; } = "baseUrl";

    /// <summary>Resolver key holding the optional bearer token. Defaults to <c>token</c>.</summary>
    public string TokenKey { get; set; } = "token";

    /// <summary>JSON serializer used by Get/Post/Put/PatchJsonAsync. Defaults to <see cref="JsonSerializerDefaults.Web"/>.</summary>
    public JsonSerializerOptions? JsonOptions { get; set; }

    /// <summary>Default number of retry attempts on transient failures. <c>0</c> disables retry.</summary>
    public int DefaultRetryCount { get; set; } = 0;

    /// <summary>Base delay between retry attempts; doubles on each successive retry.</summary>
    public TimeSpan DefaultRetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);
}
