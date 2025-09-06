using XRoadFolkWeb.Infrastructure;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace XRoadFolkWeb.Extensions
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            bool hasXRoad = !string.IsNullOrWhiteSpace(configuration["XRoad:BaseUrl"]);

            services
                .AddHttpContextAccessor()
                .AddAppLogging(configuration)
                .AddAppLocalization(configuration)
                .AddAppAntiforgery()
                .AddAppOptions(configuration)
                .AddMvcCustomizations()
                .AddResponseCompressionDefaults()
                .AddCookiePolicyDefaults()
                .AddDataProtectionDefaults(configuration)
                .AddSessionServices(configuration);

            if (hasXRoad)
            {
                services
                    .AddXRoadHttpClient()
                    .AddFolkRawClientFactory()
                    .AddPeopleServices();
            }

            return services;
        }

        private static IServiceCollection AddCookiePolicyDefaults(this IServiceCollection services)
        {
            _ = services.AddOptions<CookiePolicyOptions>()
                .Configure((CookiePolicyOptions opts) =>
                {
                    bool isDev = string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);

                    // Enforce secure defaults across all cookies
                    opts.HttpOnly = HttpOnlyPolicy.Always;
                    opts.MinimumSameSitePolicy = SameSiteMode.Lax; // safe default for top-level navigations
                    // In production, force Secure; in dev/test (HTTP, TestServer) keep SameAsRequest to avoid breaking flows
                    opts.Secure = isDev ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

                    opts.OnAppendCookie = ctx =>
                    {
                        ctx.CookieOptions.HttpOnly = true;
                        if (ctx.CookieOptions.SameSite == SameSiteMode.Unspecified)
                        {
                            ctx.CookieOptions.SameSite = SameSiteMode.Lax;
                        }
                        if (!isDev)
                        {
                            ctx.CookieOptions.Secure = true;
                        }
                        if (string.IsNullOrEmpty(ctx.CookieOptions.Path))
                        {
                            ctx.CookieOptions.Path = "/";
                        }
                    };
                    opts.OnDeleteCookie = ctx =>
                    {
                        ctx.CookieOptions.HttpOnly = true;
                        if (ctx.CookieOptions.SameSite == SameSiteMode.Unspecified)
                        {
                            ctx.CookieOptions.SameSite = SameSiteMode.Lax;
                        }
                        if (!isDev)
                        {
                            ctx.CookieOptions.Secure = true;
                        }
                        if (string.IsNullOrEmpty(ctx.CookieOptions.Path))
                        {
                            ctx.CookieOptions.Path = "/";
                        }
                    };
                });

            return services;
        }

        private static IServiceCollection AddDataProtectionDefaults(this IServiceCollection services, IConfiguration configuration)
        {
            bool isDev = string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);

            string appName = configuration["DataProtection:ApplicationName"] ?? "XRoadFolkWeb";
            var dp = services.AddDataProtection().SetApplicationName(appName);

            if (!isDev)
            {
                string? dir = configuration["DataProtection:KeysDirectory"];
                if (string.IsNullOrWhiteSpace(dir))
                {
                    dir = System.IO.Path.Combine(AppContext.BaseDirectory, "keys");
                }
                System.IO.Directory.CreateDirectory(dir);
                _ = dp.PersistKeysToFileSystem(new System.IO.DirectoryInfo(dir));

                if (OperatingSystem.IsWindows())
                {
                    _ = dp.ProtectKeysWithDpapi(protectToLocalMachine: true);
                }
                else
                {
                    string? certPath = configuration["DataProtection:Certificate:Path"];
                    string? certPwd = configuration["DataProtection:Certificate:Password"];
                    if (!string.IsNullOrWhiteSpace(certPath) && System.IO.File.Exists(certPath))
                    {
                        try
                        {
                            // CA2000: The Data Protection system holds the certificate reference for the app lifetime.
                            // Disposing it here would break encryption at runtime. Suppress and document.
#pragma warning disable CA2000 // Certificate lifetime managed by DataProtection
                            _ = dp.ProtectKeysWithCertificate(string.IsNullOrEmpty(certPwd)
                                ? new System.Security.Cryptography.X509Certificates.X509Certificate2(certPath)
                                : new System.Security.Cryptography.X509Certificates.X509Certificate2(certPath, certPwd));
#pragma warning restore CA2000
                        }
                        catch
                        {
                            // ignore and rely on file permissions/ACLs
                        }
                    }
                }
            }

            return services;
        }

        /// <summary>
        /// Configure session with safe defaults. Cookie.IsEssential defaults to false to respect consent requirements.
        /// </summary>
        private static IServiceCollection AddSessionServices(this IServiceCollection services, IConfiguration configuration)
        {
            ConfigureSessionDistributedCache(services, configuration);
            ConfigureSessionOptions(services, configuration);
            _ = services.AddSession();
            return services;
        }

        private static void ConfigureSessionDistributedCache(IServiceCollection services, IConfiguration configuration)
        {
            IConfiguration sessSection = configuration.GetSection("Session");
            string store = sessSection.GetValue<string>("Store") ?? "InMemory";
            var log = NullLogger.Instance;

            switch (store.Trim().ToLowerInvariant())
            {
                case "redis":
                    {
                        string? redisCfg = sessSection.GetValue<string>("Redis:Configuration");
                        string? instance = sessSection.GetValue<string>("Redis:InstanceName") ?? "sess:";
                        if (string.IsNullOrWhiteSpace(redisCfg))
                        {
                            log.LogWarning("Session: Store=Redis but no Redis:Configuration provided. Falling back to InMemory cache.");
                            services.AddDistributedMemoryCache();
                        }
                        else
                        {
                            services.AddStackExchangeRedisCache(o =>
                            {
                                o.Configuration = redisCfg;
                                o.InstanceName = instance;
                            });
                            log.LogInformation("Session: Using Redis distributed cache (InstanceName='{Instance}').", instance);
                        }
                    }
                    break;
                case "sqlserver":
                    {
                        string? conn = sessSection.GetValue<string>("SqlServer:ConnectionString");
                        string schema = sessSection.GetValue<string>("SqlServer:SchemaName") ?? "dbo";
                        string table = sessSection.GetValue<string>("SqlServer:TableName") ?? "SessionCache";
                        if (string.IsNullOrWhiteSpace(conn))
                        {
                            log.LogWarning("Session: Store=SqlServer but no SqlServer:ConnectionString provided. Falling back to InMemory cache.");
                            services.AddDistributedMemoryCache();
                        }
                        else
                        {
                            services.AddDistributedSqlServerCache(o =>
                            {
                                o.ConnectionString = conn;
                                o.SchemaName = schema;
                                o.TableName = table;
                            });
                            log.LogInformation("Session: Using SQL Server distributed cache (Table={Schema}.{Table}).", schema, table);
                        }
                    }
                    break;
                default:
                    services.AddDistributedMemoryCache();
                    break;
            }
        }

        private static void ConfigureSessionOptions(IServiceCollection services, IConfiguration configuration)
        {
            _ = services.AddOptions<SessionOptions>()
                .Configure<IConfiguration, ILoggerFactory>((options, cfg, lf2) =>
                {
                    ILogger log2 = lf2.CreateLogger("SessionConfig");
                    IConfiguration section = cfg.GetSection("Session");
                    ApplySessionOptions(options, section, log2);
                });
        }

        private static void ApplySessionOptions(SessionOptions options, IConfiguration section, ILogger log)
        {
            string cookieName = GetCookieName(section);
            bool cookieHttpOnly = section.GetBoolOrDefault("Cookie:HttpOnly", @default: true, logger: log);
            bool cookieIsEssential = section.GetBoolOrDefault("Cookie:IsEssential", @default: false, logger: log);
            SameSiteMode sameSite = ParseSameSite(section, log);
            CookieSecurePolicy securePolicy = ParseSecurePolicy(section, log);
            int idleMinutes = ParseIdleMinutes(section, log);

            if (cookieIsEssential)
            {
                log.LogInformation("Session: Cookie:IsEssential is true. Ensure consent/compliance requirements are met in your jurisdiction.");
            }

            options.Cookie.Name = cookieName;
            options.Cookie.HttpOnly = cookieHttpOnly;
            options.Cookie.IsEssential = cookieIsEssential;
            options.Cookie.SecurePolicy = securePolicy;
            options.Cookie.SameSite = sameSite;
            options.IdleTimeout = TimeSpan.FromMinutes(idleMinutes);
        }

        private static string GetCookieName(IConfiguration section)
        {
            return section.GetValue<string>("Cookie:Name") ?? ".XRoadFolk.Session";
        }

        private static int ParseIdleMinutes(IConfiguration section, ILogger log)
        {
            int idleMinutes = 30;
            string? idleStr = section["IdleTimeoutMinutes"];
            if (!string.IsNullOrWhiteSpace(idleStr))
            {
                if (!int.TryParse(idleStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out idleMinutes))
                {
                    log.LogWarning("Session: Invalid IdleTimeoutMinutes='{Value}'. Falling back to {Fallback} minutes.", idleStr, 30);
                    idleMinutes = 30;
                }
            }

            int origIdle = idleMinutes;
            if (idleMinutes < 1)
            {
                idleMinutes = 1;
                log.LogWarning("Session: IdleTimeoutMinutes {Original} is too small. Clamped to {Clamped} minute.", origIdle, idleMinutes);
            }
            return idleMinutes;
        }

        private static SameSiteMode ParseSameSite(IConfiguration section, ILogger log)
        {
            string? sameSiteStr = section.GetValue<string>("Cookie:SameSite");
            SameSiteMode sameSite = SameSiteMode.Lax;
            if (!string.IsNullOrWhiteSpace(sameSiteStr))
            {
                if (!Enum.TryParse(sameSiteStr, ignoreCase: true, out SameSiteMode parsedSameSite))
                {
                    log.LogWarning("Session: Invalid Cookie:SameSite='{Value}'. Falling back to {Fallback}.", sameSiteStr, sameSite);
                }
                else
                {
                    sameSite = parsedSameSite;
                }
            }
            return sameSite;
        }

        private static CookieSecurePolicy ParseSecurePolicy(IConfiguration section, ILogger log)
        {
            string? securePolicyStr = section.GetValue<string>("Cookie:SecurePolicy");
            CookieSecurePolicy securePolicy = CookieSecurePolicy.Always;
            if (!string.IsNullOrWhiteSpace(securePolicyStr))
            {
                if (!Enum.TryParse(securePolicyStr, ignoreCase: true, out CookieSecurePolicy parsedSecure))
                {
                    log.LogWarning("Session: Invalid Cookie:SecurePolicy='{Value}'. Falling back to ${Fallback}.", securePolicyStr, securePolicy);
                }
                else
                {
                    securePolicy = parsedSecure;
                }
            }
            return securePolicy;
        }
    }
}
