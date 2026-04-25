using NetworkBlaster.Interfaces;

namespace NetworkBlaster;

/// <summary>
/// Options used to register a <see cref="INetClient"/> with the DI container.
/// </summary>
/// <remarks>
/// NetworkBlaster never reads the vault directly. Callers wire a
/// <see cref="SecretResolver"/> delegate (typically <c>Secrets.Resolver</c>
/// from TaskBlaster / SecretBlast) and the client lazily pulls values
/// at request time.
/// </remarks>
public class NetworkBlasterOptions
{
    /// <summary>Delegate used to resolve named-connection secrets at runtime.</summary>
    public SecretResolver? Resolver { get; set; }

    /// <summary>Logical connection name; used as the secret category.</summary>
    public string ConnectionName { get; set; } = string.Empty;

    /// <summary>Resolver key holding the connection's base URL. Defaults to <c>baseUrl</c>.</summary>
    public string BaseUrlKey { get; set; } = "baseUrl";

    /// <summary>Resolver key holding the optional bearer token. Defaults to <c>token</c>.</summary>
    public string TokenKey { get; set; } = "token";
}
