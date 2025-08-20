using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
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
(IConfigurationRoot config, XRoadSettings xr) = loader.Load(log);

// Localization/globalization
string? cultureName = config.GetValue<string>("Localization:Culture");
if (!string.IsNullOrWhiteSpace(cultureName))
{
    try
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        log.LogInformation("[culture] Using {Culture}", culture.Name);
    }
    catch (CultureNotFoundException)
    {
        log.LogWarning("[culture] Requested culture {Culture} not found, using defaults", cultureName);
    }
}

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

PeopleService service = new(client, config, xr, log);

ServiceCollection services = new();
services.AddSingleton<ILoggerFactory>(loggerFactory);
services.AddLocalization(opts => opts.ResourcesPath = "Resources");
using ServiceProvider provider = services.BuildServiceProvider();
IStringLocalizer<ConsoleUi> localizer = provider.GetRequiredService<IStringLocalizer<ConsoleUi>>();

ConsoleUi ui = new(config, service, log, localizer);
await ui.RunAsync();
