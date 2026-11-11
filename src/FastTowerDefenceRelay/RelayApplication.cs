using FastTowerDefenceRelay.Relay;

namespace FastTowerDefenceRelay;

/// <summary>Builds the console relay host, with the HTTP room listing when it is enabled.</summary>
internal static class RelayApplication
{
    /// <param name="httpPort">TCP port of the HTTP room listing, zero picks a free port, null disables it.</param>
    /// <exception cref="ArgumentException">Invalid options, or a room listing of a token-restricted relay.</exception>
    public static IHost Build(RelayOptions options, int? httpPort, Action<ILoggingBuilder>? configureLogging = null)
    {
        // Without the listing there is no HTTP server at all.
        if (httpPort is null)
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                ContentRootPath = AppContext.BaseDirectory,
            });
            ConfigureLogging(builder.Logging, configureLogging);
            builder.Services.AddRelay(options);
            return builder.Build();
        }

        var web = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
        });
        web.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(ListenAddress.Resolve(options.Listen), httpPort.Value));
        ConfigureLogging(web.Logging, configureLogging);
        web.Services.AddRelay(options);
        var app = web.Build();
        app.MapRoomListing();
        return app;
    }

    private static void ConfigureLogging(ILoggingBuilder logging, Action<ILoggingBuilder>? configureLogging)
    {
        logging.ClearProviders();
        logging.AddSimpleConsole(console =>
        {
            console.SingleLine = true;
            console.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
        });
        configureLogging?.Invoke(logging);
    }
}
