using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XRoadFolkWeb.Infrastructure;

namespace XRoadFolkWeb.Extensions;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppLogging(this IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddFilter("Microsoft", LogLevel.Warning);
        });
        return services;
    }

    // Registers IHttpLogStore and the custom logger provider.
    // Chooses file-backed store with background writer when configured, otherwise in-memory.
    public static IServiceCollection AddHttpLogging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<HttpLogOptions>()
                .BindConfiguration("HttpLogs")
                .Validate(o => o.Capacity >= 50, "HttpLogs:Capacity must be >= 50")
                .ValidateOnStart();

        services.AddSingleton<ILogStream, LogStreamBroadcaster>();

        // Read configuration at registration time to decide which implementation to use
        HttpLogOptions opts = new();
        configuration.GetSection("HttpLogs").Bind(opts);

        if (opts.PersistToFile && !string.IsNullOrWhiteSpace(opts.FilePath))
        {
            // File-backed store + background writer
            services.AddSingleton<FileBackedHttpLogStore>();
            services.AddSingleton<IHttpLogStore>(sp => sp.GetRequiredService<FileBackedHttpLogStore>());
            services.AddHostedService<FileBackedLogWriter>();
        }
        else
        {
            // Default: in-memory store
            services.AddSingleton<IHttpLogStore>(sp => new InMemoryHttpLog(sp.GetRequiredService<IConfiguration>()));
        }

        // Register the custom logger provider so all logs also flow into IHttpLogStore
        services.AddSingleton<ILoggerProvider>(sp => new InMemoryHttpLogLoggerProvider(sp.GetRequiredService<IHttpLogStore>()));
        return services;
    }
}
