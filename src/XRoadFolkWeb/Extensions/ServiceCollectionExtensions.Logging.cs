using Microsoft.Extensions.Options;
using XRoadFolkWeb.Infrastructure;

namespace XRoadFolkWeb.Extensions
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAppLogging(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            _ = services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddConsole();
                builder.AddDebug();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // HttpLog options + validation
            _ = services.AddOptions<HttpLogOptions>()
                    .BindConfiguration("HttpLogs")
                    .Validate(o => o.Capacity >= 50, "HttpLogs:Capacity must be >= 50")
                    .Validate(o => o.MaxQueue >= 100, "HttpLogs:MaxQueue must be >= 100 for file-backed store")
                    .Validate(o => !o.PersistToFile || !string.IsNullOrWhiteSpace(o.FilePath), "HttpLogs:FilePath must be set when PersistToFile is true")
                    .ValidateOnStart();

            _ = services.AddSingleton<ILogFeed, LogStreamBroadcaster>();

            // Read configuration at registration time to decide which implementation to use
            HttpLogOptions opts = new();
            services.BuildServiceProvider().GetRequiredService<IConfiguration>().GetSection("HttpLogs").Bind(opts);

            if (opts.PersistToFile && !string.IsNullOrWhiteSpace(opts.FilePath))
            {
                // File-backed store + background writer
                _ = services.AddSingleton<FileBackedHttpLogStore>();
                _ = services.AddSingleton<IHttpLogStore>(static sp => sp.GetRequiredService<FileBackedHttpLogStore>());
                _ = services.AddHostedService<FileBackedLogWriter>();
            }
            else
            {
                // Default: in-memory store
                _ = services.AddSingleton<IHttpLogStore>(static sp => new InMemoryHttpLog(sp.GetRequiredService<IOptions<HttpLogOptions>>()));
            }

            // Register the custom logger provider so all logs also flow into IHttpLogStore
            _ = services.AddSingleton<ILoggerProvider>(static sp => new InMemoryHttpLogLoggerProvider(sp.GetRequiredService<IHttpLogStore>()));
            return services;
        }
    }
}
