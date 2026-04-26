using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetworkBlast.Interfaces;

namespace NetworkBlast;

/// <summary>
/// Extension methods for registering NetworkBlast services with a DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="INetClient"/> singleton bound to the connection
    /// described by <paramref name="configure"/>.
    /// </summary>
    /// <param name="services">The target <see cref="IServiceCollection"/>.</param>
    /// <param name="configure">Delegate that populates <see cref="NetworkBlastOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddNetworkBlast(o =>
    /// {
    ///     o.Resolver       = Secrets.Resolver;
    ///     o.ConnectionName = "github";
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddNetworkBlast(this IServiceCollection services, Action<NetworkBlastOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new NetworkBlastOptions();
        configure(options);

        if (options.Resolver is null)
            throw new InvalidOperationException("NetworkBlast: Resolver must be configured.");
        if (string.IsNullOrWhiteSpace(options.ConnectionName))
            throw new InvalidOperationException("NetworkBlast: ConnectionName must be configured.");

        services.TryAddSingleton<INetClient>(_ =>
            new NetClient(
                options.Resolver,
                options.ConnectionName,
                httpClient: null,
                baseUrlKey: options.BaseUrlKey,
                tokenKey: options.TokenKey,
                jsonOptions: options.JsonOptions,
                defaultRetryCount: options.DefaultRetryCount,
                defaultRetryBaseDelay: options.DefaultRetryBaseDelay));

        return services;
    }
}
