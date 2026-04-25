using System;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkBlaster;

/// <summary>
/// Pluggable secret resolver shape used to hydrate named-connection values
/// (base URL, auth token, headers) on demand.
/// </summary>
/// <remarks>
/// Identical in shape to the resolver exposed by SecretBlast / TaskBlaster
/// (<c>Func&lt;category, key, CancellationToken, Task&lt;string&gt;&gt;</c>),
/// but NetworkBlaster takes a delegate so it has zero dependency on any
/// particular vault implementation.
/// </remarks>
/// <param name="category">Logical grouping for the secret (typically the connection name).</param>
/// <param name="key">Field within the category, e.g. <c>baseUrl</c> or <c>token</c>.</param>
/// <param name="cancellationToken">Cancellation propagated from the calling request.</param>
/// <returns>The resolved secret value.</returns>
public delegate Task<string> SecretResolver(string category, string key, CancellationToken cancellationToken);
