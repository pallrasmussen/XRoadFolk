using System.Globalization;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Logging;
using XRoadFolkRaw.Lib.Options;
using XRoadFolkWeb.Infrastructure;

namespace XRoadFolkWeb.Extensions;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Decompose into focused registrations
        services
            .AddAppLogging()
            .AddAppLocalization(configuration)
            .AddAppAntiforgery()
            .AddAppOptions(configuration)
            .AddXRoadHttpClient()
            .AddFolkRawClientFactory()
            .AddPeopleServices()
            .AddMvcCustomizations()
            .AddInMemoryHttpLogging();

        // Response compression
        services.AddResponseCompression(opts =>
        {
            opts.EnableForHttps = true;
            opts.MimeTypes = XRoadFolkWeb.Program.ResponseCompressionMimeTypes;
        });

        return services;
    }
}
