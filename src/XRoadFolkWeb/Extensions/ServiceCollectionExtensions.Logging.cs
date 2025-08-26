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

    public static IServiceCollection AddInMemoryHttpLogging(this IServiceCollection services)
    {
        // Bind options from configuration (optional)
        services.AddOptions<HttpLogOptions>()
                .BindConfiguration("HttpLogs")
                .Validate(o => o.Capacity >= 50, "HttpLogs:Capacity must be >= 50")
                .ValidateOnStart();

        services.AddSingleton<ILogStream, LogStreamBroadcaster>();
        services.AddSingleton<IHttpLogStore>(sp => new InMemoryHttpLogStore(
            sp.GetRequiredService<IOptions<HttpLogOptions>>(),
            sp.GetRequiredService<ILogStream>()));
        services.AddSingleton<ILoggerProvider>(sp => new InMemoryHttpLogLoggerProvider(sp.GetRequiredService<IHttpLogStore>()));
        return services;
    }
}
