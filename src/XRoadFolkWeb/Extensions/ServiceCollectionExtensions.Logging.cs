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

            Microsoft.Extensions.Options.OptionsBuilder<HttpLogOptions> optionsBuilder = services.AddOptions<HttpLogOptions>()
                    .BindConfiguration("HttpLogs")
                    .Validate(o => o.Capacity >= 50, "HttpLogs:Capacity must be >= 50")
                    .ValidateOnStart();

            _ = services.AddSingleton<ILogFeed, LogStreamBroadcaster>();

            // Read configuration at registration time to decide which implementation to use
            HttpLogOptions opts = new();
            configuration.GetSection("HttpLogs").Bind(opts);

            if (opts.PersistToFile && !string.IsNullOrWhiteSpace(opts.FilePath))
            {
                // File-backed store + background writer
                _ = services.AddSingleton<FileBackedHttpLogStore>();
                IServiceCollection serviceCollection = services.AddSingleton<IHttpLogStore>(static sp => sp.GetRequiredService<FileBackedHttpLogStore>());
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
