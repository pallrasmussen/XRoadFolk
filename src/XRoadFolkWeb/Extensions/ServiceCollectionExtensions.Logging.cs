using Microsoft.Extensions.Logging;
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
        services.AddSingleton<ILogStream, LogStreamBroadcaster>();
        services.AddSingleton<IHttpLogStore>(sp => new InMemoryHttpLogStore(1000, sp.GetRequiredService<ILogStream>()));
        services.AddSingleton<ILoggerProvider>(sp => new InMemoryHttpLogLoggerProvider(sp.GetRequiredService<IHttpLogStore>()));
        return services;
    }
}
