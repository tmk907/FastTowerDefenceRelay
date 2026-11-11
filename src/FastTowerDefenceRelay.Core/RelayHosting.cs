using FastTowerDefenceRelay.Http;
using FastTowerDefenceRelay.Relay;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace FastTowerDefenceRelay;

/// <summary>Adds the relay and its room listing to an application.</summary>
public static class RelayHosting
{
    /// <summary>Run the relay as a background service of the host.</summary>
    /// <exception cref="ArgumentException">Invalid options.</exception>
    public static IServiceCollection AddRelay(this IServiceCollection services, RelayOptions options)
    {
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton<RoomDirectory>();
        services.AddSingleton<RelayServer>();
        services.AddHostedService(provider => provider.GetRequiredService<RelayServer>());
        return services;
    }

    /// <summary>Serve the open rooms as JSON at GET /rooms, see docs/http-api.md.</summary>
    /// <exception cref="ArgumentException">The relay is token-restricted, so its rooms are private.</exception>
    public static RouteHandlerBuilder MapRoomListing(this IEndpointRouteBuilder endpoints)
    {
        if (endpoints.ServiceProvider.GetRequiredService<RelayOptions>().RequiresToken)
        {
            throw new ArgumentException("the HTTP room listing requires a public relay, rooms of a token-restricted relay are private");
        }
        return RoomsEndpoint.Map(endpoints);
    }
}
