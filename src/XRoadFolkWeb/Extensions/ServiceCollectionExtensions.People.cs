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
            FolkRawClient client   = sp.GetRequiredService<FolkRawClient>();
            IConfiguration cfg      = sp.GetRequiredService<IConfiguration>();
            XRoadSettings xr       = sp.GetRequiredService<XRoadSettings>();
            ILogger<PeopleService> logger   = sp.GetRequiredService<ILogger<PeopleService>>();
            IStringLocalizer<PeopleService> loc      = sp.GetRequiredService<IStringLocalizer<PeopleService>>();
            IValidateOptions<GetPersonRequestOptions> validator = sp.GetRequiredService<IValidateOptions<GetPersonRequestOptions>>();
            return new PeopleService(client, cfg, xr, logger, loc, validator);
        });
        return services;
    }
}
