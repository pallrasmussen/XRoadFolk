using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XRoad.Config;
using XRoadFolkRaw;
using XRoadFolkRaw.Lib;

// Top-level program

// Logger
using ILoggerFactory loggerFactory = LoggerFactory.Create(b =>
{
    _ = b.AddConsole();
    _ = b.SetMinimumLevel(LogLevel.Information);
    _ = b.AddFilter("Microsoft", LogLevel.Warning);
});
ILogger log = loggerFactory.CreateLogger("XRoadFolkRaw");
using IDisposable _corr = LoggingHelper.BeginCorrelationScope(log);

// Configuration
ConfigurationLoader loader = new();
ServiceCollection preServices = new();
preServices.AddSingleton<ILoggerFactory>(loggerFactory);
preServices.AddLocalization(opts => opts.ResourcesPath = "Resources");
using ServiceProvider preProvider = preServices.BuildServiceProvider();
IStringLocalizer<ConfigurationLoader> cfgLocalizer = preProvider.GetRequiredService<IStringLocalizer<ConfigurationLoader>>();
(IConfigurationRoot config, XRoadSettings xr) = loader.Load(log, cfgLocalizer);
IStringLocalizer<PeopleService> serviceLocalizer = preProvider.GetRequiredService<IStringLocalizer<PeopleService>>();

// Startup banner
Console.WriteLine("Press Ctrl+Q at any time to quit.\n");

// Load certificate
System.Security.Cryptography.X509Certificates.X509Certificate2 cert = CertLoader.LoadFromConfig(xr.Certificate);
log.LogInformation("[cert] Using {Subject} thumbprint {Thumbprint}", cert.Subject, cert.Thumbprint);

// Create raw client
bool verbose = config.GetValue("Logging:Verbose", false);
bool maskTokens = config.GetValue("Logging:MaskTokens", true);
int httpAttempts = config.GetValue("Retry:Http:Attempts", 3);
int httpBaseDelay = config.GetValue("Retry:Http:BaseDelayMs", 200);
int httpJitter = config.GetValue("Retry:Http:JitterMs", 250);

using FolkRawClient client = new(
    xr.BaseUrl, cert, TimeSpan.FromSeconds(xr.Http.TimeoutSeconds),
    logger: log, verbose: verbose, maskTokens: maskTokens,
    retryAttempts: httpAttempts, retryBaseDelayMs: httpBaseDelay, retryJitterMs: httpJitter);

PeopleService service = new(client, config, xr, log, serviceLocalizer);

ServiceCollection services = new();
services.AddSingleton<ILoggerFactory>(loggerFactory);
services.AddLocalization(opts => opts.ResourcesPath = "Resources");
string[] supportedCultureNames = config.GetSection("Localization:SupportedCultures").Get<string[]>() ?? ["en-US"];
services.Configure<RequestLocalizationOptions>(opts =>
{
    List<CultureInfo> cultures = supportedCultureNames.Select(CultureInfo.GetCultureInfo).ToList();
    string defaultName = config.GetValue<string>("Localization:Culture") ?? cultures.First().Name;
    opts.SupportedCultures = cultures;
    opts.SupportedUICultures = cultures;
    opts.DefaultRequestCulture = new RequestCulture(defaultName);
});

using ServiceProvider provider = services.BuildServiceProvider();
RequestLocalizationOptions locOpts = provider.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
CultureInfo culture = locOpts.DefaultRequestCulture.Culture;
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;
log.LogInformation("[culture] Using {Culture}", culture.Name);
IStringLocalizer<ConsoleUi> localizer = provider.GetRequiredService<IStringLocalizer<ConsoleUi>>();
IStringLocalizer<InputValidation> valLocalizer = provider.GetRequiredService<IStringLocalizer<InputValidation>>();
LocalizedString check = localizer["BannerSeparator"];
if (check.ResourceNotFound)
{
    LogMissingBannerSeparator(log, culture.Name);
}

ConsoleUi ui = new(config, service, log, localizer, valLocalizer);
await ui.RunAsync();

static partial class Program
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
                   Message = "[culture] Resource 'BannerSeparator' not found for {Culture}")]
    public static partial void LogMissingBannerSeparator(ILogger logger, string culture);
}
