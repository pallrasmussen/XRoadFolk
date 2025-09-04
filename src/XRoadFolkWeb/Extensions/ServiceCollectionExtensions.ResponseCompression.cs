using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;

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
                opts.Providers.Add<BrotliCompressionProvider>();
                opts.Providers.Add<GzipCompressionProvider>();
            });

            _ = services.Configure<BrotliCompressionProviderOptions>(o =>
            {
                o.Level = CompressionLevel.Optimal;
            });
            _ = services.Configure<GzipCompressionProviderOptions>(o =>
            {
                o.Level = CompressionLevel.Fastest;
            });

            return services;
        }
    }
}
