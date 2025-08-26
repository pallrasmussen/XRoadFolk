using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace XRoadFolkWeb.Extensions;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Decompose into focused registrations
        services
            .AddAppLogging()
            .AddAppLocalization(configuration)
            .AddAppAntiforgery()
            .AddAppOptions(configuration)
            .AddXRoadHttpClient()
            .AddFolkRawClientFactory()
            .AddPeopleServices()
            .AddMvcCustomizations()
            .AddInMemoryHttpLogging();

        // Response compression
        services.AddResponseCompression(opts =>
        {
            opts.EnableForHttps = true;
            opts.MimeTypes = XRoadFolkWeb.Program.ResponseCompressionMimeTypes;
        });

        return services;
    }
}
