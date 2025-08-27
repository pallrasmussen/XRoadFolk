namespace XRoadFolkWeb.Extensions
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
        {
            return services
                .AddAppLogging()
                .AddAppLocalization(configuration)
                .AddAppAntiforgery()
                .AddAppOptions(configuration)
                .AddXRoadHttpClient()
                .AddFolkRawClientFactory()
                .AddPeopleServices()
                .AddMvcCustomizations()
                .AddHttpLogging(configuration)
                .AddResponseCompressionDefaults();
        }
    }
}
