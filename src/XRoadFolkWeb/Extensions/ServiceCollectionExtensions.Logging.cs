using XRoadFolkWeb.Infrastructure;

namespace XRoadFolkWeb.Extensions
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAppLogging(this IServiceCollection services)
        {
            _ = services.AddLogging(builder =>
            {
                _ = builder.ClearProviders();
                _ = builder.AddSimpleConsole(options =>
                {
                    options.UseUtcTimestamp = false; // local time
                    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fff zzz ";
                });
                _ = builder.SetMinimumLevel(LogLevel.Information);
                _ = builder.AddFilter("Microsoft", LogLevel.Warning);
            });
            return services;
        }

        /// <summary>
        /// Registers IHttpLogStore and the custom logger provider.
        /// Chooses file-backed store with background writer when configured, otherwise in-memory.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configuration"></param>
        /// <returns></returns>
        public static IServiceCollection AddHttpLogging(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            _ = services.AddOptions<HttpLogOptions>()
                    .BindConfiguration("HttpLogs")
                    .Validate(o => o.Capacity >= 50, "HttpLogs:Capacity must be >= 50")
                    .Validate(o => o.MaxWritesPerSecond >= 0, "HttpLogs:MaxWritesPerSecond must be >= 0")
                    .Validate(o => o.MaxFileBytes >= 50_000, "HttpLogs:MaxFileBytes must be >= 50000 bytes")
                    .Validate(o => o.MaxRolls >= 1, "HttpLogs:MaxRolls must be >= 1")
                    .Validate(o => o.MaxQueue >= 100, "HttpLogs:MaxQueue must be >= 100")
                    .Validate(o => o.FlushIntervalMs >= 50, "HttpLogs:FlushIntervalMs must be >= 50 ms")
                    .Validate(o => !o.PersistToFile || !string.IsNullOrWhiteSpace(o.FilePath), "HttpLogs:FilePath must be set when PersistToFile is true")
                    .ValidateOnStart();

            _ = services.AddSingleton<ILogFeed, LogStreamBroadcaster>();

            // Read configuration at registration time to decide which implementation to use
            HttpLogOptions opts = new();
            configuration.GetSection("HttpLogs").Bind(opts);

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
                _ = services.AddSingleton<IHttpLogStore>(static sp => new InMemoryHttpLog(sp.GetRequiredService<IConfiguration>()));
            }

            // Register the custom logger provider so all logs also flow into IHttpLogStore
            _ = services.AddSingleton<ILoggerProvider>(static sp => new InMemoryHttpLogLoggerProvider(sp.GetRequiredService<IHttpLogStore>()));
            return services;
        }
    }
}
