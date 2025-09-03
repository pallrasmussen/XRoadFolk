namespace XRoadFolkWeb.Extensions
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddResponseCompressionDefaults(this IServiceCollection services)
        {
            _ = services.AddResponseCompression(static opts =>
            {
                opts.EnableForHttps = true;
                opts.MimeTypes = Shared.ProgramStatics.ResponseCompressionMimeTypes;
            });
            return services;
        }
    }
}
