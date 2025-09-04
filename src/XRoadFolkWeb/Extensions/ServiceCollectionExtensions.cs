using XRoadFolkWeb.Infrastructure;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.CookiePolicy;

namespace XRoadFolkWeb.Extensions
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
        {
            return services
                .AddAppLogging(configuration)
                .AddAppLocalization(configuration)
                .AddAppAntiforgery()
                .AddAppOptions(configuration)
                .AddXRoadHttpClient()
                .AddFolkRawClientFactory()
                .AddPeopleServices()
                .AddMvcCustomizations()
                .AddResponseCompressionDefaults()
                .AddCookiePolicyDefaults()
                .AddSessionServices(configuration);
        }

        private static IServiceCollection AddCookiePolicyDefaults(this IServiceCollection services)
        {
            _ = services.AddOptions<CookiePolicyOptions>()
                .Configure<IHostEnvironment>((opts, env) =>
                {
                    // Enforce secure defaults across all cookies
                    opts.HttpOnly = HttpOnlyPolicy.Always;
                    opts.MinimumSameSitePolicy = SameSiteMode.Lax; // safe default for top-level navigations
                    // In production, force Secure; in dev/test (HTTP, TestServer) keep SameAsRequest to avoid breaking flows
                    opts.Secure = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

                    // Optionally, you can validate/normalize cookies as they are appended/deleted
                    opts.OnAppendCookie = ctx =>
                    {
                        ctx.CookieOptions.HttpOnly = true;
                        if (ctx.CookieOptions.SameSite == SameSiteMode.Unspecified)
                        {
                            ctx.CookieOptions.SameSite = SameSiteMode.Lax;
                        }
                        if (!env.IsDevelopment())
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
                        // Mirror the same policy on deletion to ensure the browser accepts the deletion
                        ctx.CookieOptions.HttpOnly = true;
                        if (ctx.CookieOptions.SameSite == SameSiteMode.Unspecified)
                        {
                            ctx.CookieOptions.SameSite = SameSiteMode.Lax;
                        }
                        if (!env.IsDevelopment())
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

        /// <summary>
        /// Configure session with safe defaults. Cookie.IsEssential defaults to false to respect consent requirements.
        /// Store selection (Session:Store): InMemory (default), Redis, or SqlServer.
        /// - InMemory: simple, fast, but not shared across instances; not suitable for multi-node production.
        /// - Redis: shared, scalable, good for multi-node; requires Redis endpoint; set Session:Redis:Configuration.
        /// - SqlServer: shared and durable; higher latency than Redis; set Session:SqlServer:ConnectionString (and optional SchemaName/TableName).
        /// IdleTimeout is SLIDING expiration for the session data; cookie persistence remains session cookie unless Cookie.MaxAge is set.
        /// If you set Cookie:IsEssential=true, ensure you have a documented legal basis and/or consent in place.
        /// </summary>
        private static IServiceCollection AddSessionServices(this IServiceCollection services, IConfiguration configuration)
        {
            // Choose backing store
            IConfiguration sessSection = configuration.GetSection("Session");
            string store = sessSection.GetValue<string>("Store") ?? "InMemory";

            ILoggerFactory lf = services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();
            ILogger log = lf.CreateLogger("SessionConfig");

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
                    // Emit a reminder for non-dev environments (best-effort).
                    string env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? string.Empty;
                    if (!env.Equals("Development", StringComparison.OrdinalIgnoreCase))
                    {
                        log.LogInformation("Session: Using in-memory cache. For multi-instance production deployments, configure Session:Store=Redis or SqlServer.");
                    }
                    break;
            }

            _ = services.AddOptions<SessionOptions>()
                .Configure<IConfiguration, ILoggerFactory>((options, cfg, lf2) =>
                {
                    ILogger log2 = lf2.CreateLogger("SessionConfig");

                    IConfiguration section = cfg.GetSection("Session");

                    string cookieName = section.GetValue<string>("Cookie:Name") ?? ".XRoadFolk.Session";

                    // Use shared configuration helpers for booleans
                    bool cookieHttpOnly = section.GetBoolOrDefault("Cookie:HttpOnly", true, log2);
                    bool cookieIsEssential = section.GetBoolOrDefault("Cookie:IsEssential", false, log2); // default false for consent-friendly behavior

                    // Enums (with validation)
                    string? sameSiteStr = section.GetValue<string>("Cookie:SameSite");
                    string? securePolicyStr = section.GetValue<string>("Cookie:SecurePolicy");

                    // Idle timeout minutes (validate integer) - SLIDING expiration for session data
                    int idleMinutes = 30;
                    string? idleStr = section["IdleTimeoutMinutes"];
                    if (!string.IsNullOrWhiteSpace(idleStr))
                    {
                        if (!int.TryParse(idleStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out idleMinutes))
                        {
                            log2.LogWarning("Session: Invalid IdleTimeoutMinutes='{Value}'. Falling back to {Fallback} minutes.", idleStr, 30);
                            idleMinutes = 30;
                        }
                    }

                    SameSiteMode sameSite = SameSiteMode.Strict;
                    if (!string.IsNullOrWhiteSpace(sameSiteStr))
                    {
                        if (!Enum.TryParse(sameSiteStr, ignoreCase: true, out SameSiteMode parsedSameSite))
                        {
                            log2.LogWarning("Session: Invalid Cookie:SameSite='{Value}'. Falling back to {Fallback}.", sameSiteStr, sameSite);
                        }
                        else
                        {
                            sameSite = parsedSameSite;
                        }
                    }

                    CookieSecurePolicy securePolicy = CookieSecurePolicy.Always;
                    if (!string.IsNullOrWhiteSpace(securePolicyStr))
                    {
                        if (!Enum.TryParse(securePolicyStr, ignoreCase: true, out CookieSecurePolicy parsedSecure))
                        {
                            log2.LogWarning("Session: Invalid Cookie:SecurePolicy='{Value}'. Falling back to ${Fallback}.", securePolicyStr, securePolicy);
                        }
                        else
                        {
                            securePolicy = parsedSecure;
                        }
                    }

                    int origIdle = idleMinutes;
                    if (idleMinutes < 1)
                    {
                        idleMinutes = 1;
                        log2.LogWarning("Session: IdleTimeoutMinutes {Original} is too small. Clamped to {Clamped} minute.", origIdle, idleMinutes);
                    }

                    if (cookieIsEssential)
                    {
                        log2.LogInformation("Session: Cookie:IsEssential is true. Ensure consent/compliance requirements are met in your jurisdiction.");
                    }

                    options.Cookie.Name = cookieName;
                    options.Cookie.HttpOnly = cookieHttpOnly;
                    options.Cookie.IsEssential = cookieIsEssential;
                    options.Cookie.SecurePolicy = securePolicy;
                    options.Cookie.SameSite = sameSite;
                    // NOTE: Session uses SLIDING expiration equal to IdleTimeout; cookie remains a session cookie unless MaxAge is set explicitly.
                    options.IdleTimeout = TimeSpan.FromMinutes(idleMinutes);
                });

            _ = services.AddSession();
            return services;
        }
    }
}
