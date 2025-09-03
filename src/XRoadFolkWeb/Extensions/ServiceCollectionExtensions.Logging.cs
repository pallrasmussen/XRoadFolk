using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using XRoadFolkWeb.Infrastructure;

namespace XRoadFolkWeb.Extensions
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAppLogging(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            _ = services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddConfiguration(configuration.GetSection("Logging"));
                builder.AddConsole();
                builder.AddDebug();
                // Do not hardcode minimum level; defer to configuration
            });

            // HttpLog options + validation
            _ = services.AddOptions<HttpLogOptions>()
                    .BindConfiguration("HttpLogs")
                    .Validate(o => o.Capacity >= 50, "HttpLogs:Capacity must be >= 50")
                    .Validate(o => o.MaxQueue >= 100, "HttpLogs:MaxQueue must be >= 100 for file-backed store")
                    .Validate(o => !o.PersistToFile || !string.IsNullOrWhiteSpace(o.FilePath), "HttpLogs:FilePath must be set when PersistToFile is true")
                    .ValidateOnStart();

            _ = services.AddSingleton<ILogFeed, LogStreamBroadcaster>();

            // Register file-backed store; it will only be used if enabled via options
            _ = services.AddSingleton<FileBackedHttpLogStore>();

            // Decide store implementation at resolve time, avoiding premature ServiceProvider construction
            _ = services.AddSingleton<IHttpLogStore>(sp =>
            {
                HttpLogOptions opts = sp.GetRequiredService<IOptions<HttpLogOptions>>().Value;
                if (opts.PersistToFile && !string.IsNullOrWhiteSpace(opts.FilePath))
                {
                    return sp.GetRequiredService<FileBackedHttpLogStore>();
                }
                return new InMemoryHttpLog(sp.GetRequiredService<IOptions<HttpLogOptions>>());
            });

            // Conditionally create the background writer when file persistence is enabled
            _ = services.AddSingleton<IHostedService>(sp =>
            {
                HttpLogOptions opts = sp.GetRequiredService<IOptions<HttpLogOptions>>().Value;
                if (opts.PersistToFile && !string.IsNullOrWhiteSpace(opts.FilePath))
                {
                    return new FileBackedLogWriter(sp.GetRequiredService<FileBackedHttpLogStore>(), sp.GetRequiredService<IOptions<HttpLogOptions>>());
                }
                return new NoopHostedService();
            });

            // Register the custom logger provider so all logs also flow into IHttpLogStore
            _ = services.AddSingleton<ILoggerProvider>(sp => new InMemoryHttpLogLoggerProvider(sp.GetRequiredService<IHttpLogStore>()));
            return services;
        }

        private sealed class NoopHostedService : IHostedService
        {
            public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
