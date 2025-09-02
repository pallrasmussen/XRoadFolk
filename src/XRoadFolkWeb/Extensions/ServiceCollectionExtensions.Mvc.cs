namespace XRoadFolkWeb.Extensions;

public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// MVC/Razor Pages specific customizations
    /// </summary>
    /// <param name="services"></param>
    public static IServiceCollection AddMvcCustomizations(this IServiceCollection services)
    {
        _ = services.AddControllers(options => options.ModelBinderProviders.Insert(0, new Validation.TrimDigitsModelBinderProvider()));
        return services;
    }
}
