using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetworkBlaster.Interfaces;

namespace NetworkBlaster;

/// <summary>
/// Extension methods for registering NetworkBlaster services with a DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="INetClient"/> singleton bound to the connection
    /// described by <paramref name="configure"/>.
    /// </summary>
    /// <param name="services">The target <see cref="IServiceCollection"/>.</param>
    /// <param name="configure">Delegate that populates <see cref="NetworkBlasterOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddNetworkBlaster(o =>
    /// {
    ///     o.Resolver       = Secrets.Resolver;
    ///     o.ConnectionName = "github";
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddNetworkBlaster(this IServiceCollection services, Action<NetworkBlasterOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new NetworkBlasterOptions();
        configure(options);

        if (options.Resolver is null)
            throw new InvalidOperationException("NetworkBlaster: Resolver must be configured.");
        if (string.IsNullOrWhiteSpace(options.ConnectionName))
            throw new InvalidOperationException("NetworkBlaster: ConnectionName must be configured.");

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
