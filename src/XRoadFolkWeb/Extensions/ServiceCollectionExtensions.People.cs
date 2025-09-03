using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Options;
using XRoadFolkWeb.Features.Index;
using XRoadFolkWeb.Features.People;

namespace XRoadFolkWeb.Extensions;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddPeopleServices(this IServiceCollection services)
    {
        services.AddMemoryCache();

        // Parser is stateless; register as singleton
        services.AddSingleton<PeopleResponseParser>();

        // Index page helpers
        services.AddScoped<PeopleSearchCoordinator>();
        services.AddScoped<PersonDetailsProvider>();

        // Let DI construct PeopleService and resolve its dependencies
        services.AddScoped<PeopleService>();
        return services;
    }
}
