using FastTowerDefenceRelay.Http;
using FastTowerDefenceRelay.Relay;

namespace FastTowerDefenceRelay;

/// <summary>Builds the relay host, with the HTTP room listing when it is enabled.</summary>
internal static class RelayApplication
{
    /// <exception cref="ArgumentException">Invalid options.</exception>
    public static IHost Build(RelayOptions options, Action<ILoggingBuilder>? configureLogging = null)
    {
        options.Validate();

        // Without the listing there is no HTTP server at all.
        if (options.HttpPort is not { } httpPort)
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                ContentRootPath = AppContext.BaseDirectory,
            });
            AddRelay(builder.Services, builder.Logging, options, configureLogging);
            return builder.Build();
        }

        var web = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
        });
        web.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(ListenAddress.Resolve(options.Listen), httpPort));
        AddRelay(web.Services, web.Logging, options, configureLogging);
        var app = web.Build();
        RoomsEndpoint.Map(app);
        return app;
    }

    private static void AddRelay(IServiceCollection services, ILoggingBuilder logging, RelayOptions options,
                                 Action<ILoggingBuilder>? configureLogging)
    {
        logging.ClearProviders();
        logging.AddSimpleConsole(console =>
        {
            console.SingleLine = true;
            console.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
        });
        configureLogging?.Invoke(logging);

        services.AddSingleton(options);
        services.AddSingleton<RoomDirectory>();
        services.AddSingleton<RelayServer>();
        services.AddHostedService(provider => provider.GetRequiredService<RelayServer>());
    }
}
