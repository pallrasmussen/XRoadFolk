namespace XRoadFolkWeb.Extensions;

public static partial class ServiceCollectionExtensions
{
    // MVC/Razor Pages specific customizations
    public static IServiceCollection AddMvcCustomizations(this IServiceCollection services)
    {
        services.AddControllers(options =>
        {
            options.ModelBinderProviders.Insert(0, new XRoadFolkWeb.Validation.TrimDigitsModelBinderProvider());
        });
        return services;
    }
}
