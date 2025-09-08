using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Hosting;

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

            // Always exclude Server-Sent Events from compression
            _ = services.AddOptions<ResponseCompressionOptions>()
                .PostConfigure((ResponseCompressionOptions opts) =>
                {
                    if (opts.MimeTypes is not null)
                    {
                        opts.MimeTypes = opts.MimeTypes.Where(m => !string.Equals(m, "text/event-stream", StringComparison.OrdinalIgnoreCase)).ToArray();
                    }
                })
                // In Development, exclude text/html to avoid interfering with Browser Link script injection
                .PostConfigure<IHostEnvironment>((opts, env) =>
                {
                    if (env.IsDevelopment() && opts.MimeTypes is not null)
                    {
                        opts.MimeTypes = opts.MimeTypes.Where(m => !string.Equals(m, "text/html", StringComparison.OrdinalIgnoreCase)).ToArray();
                    }
                });

            return services;
        }
    }
}
