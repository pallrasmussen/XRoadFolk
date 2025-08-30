using XRoadFolkRaw.Lib.Extensions;

namespace XRoadFolkWeb.Extensions
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddXRoadHttpClient(this IServiceCollection services)
        {
            // Delegate to the Lib project which owns all certificate processing
            return XRoadFolkRaw.Lib.Extensions.ServiceCollectionExtensions.AddXRoadHttpClient(services);
        }
    }
}
