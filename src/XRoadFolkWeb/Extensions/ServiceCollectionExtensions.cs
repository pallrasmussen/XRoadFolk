

namespace XRoadFolkWeb.Extensions
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
        {
            // Decompose into focused registrations

            _ = services
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
            _ = services.AddResponseCompression(static opts =>
            {
                opts.EnableForHttps = true;
                opts.MimeTypes = Program.ResponseCompressionMimeTypes;
            });

            return services;
        }
    }
}
