using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Options;

namespace XRoadFolkWeb.Extensions;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddPeopleServices(this IServiceCollection services)
    {
        services.AddScoped(sp =>
        {
            var client   = sp.GetRequiredService<FolkRawClient>();
            var cfg      = sp.GetRequiredService<IConfiguration>();
            var xr       = sp.GetRequiredService<XRoadSettings>();
            var logger   = sp.GetRequiredService<ILogger<PeopleService>>();
            var loc      = sp.GetRequiredService<IStringLocalizer<PeopleService>>();
            var validator= sp.GetRequiredService<IValidateOptions<GetPersonRequestOptions>>();
            return new PeopleService(client, cfg, xr, logger, loc, validator);
        });
        return services;
    }
}
