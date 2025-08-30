using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using XRoadFolkWeb.Infrastructure;
using XRoadFolkWeb.Shared;
using System.Text.RegularExpressions;

namespace XRoadFolkWeb.Extensions
{
    public static partial class WebApplicationExtensions
    {
        // LoggerMessage delegates for performance
        private static readonly Action<ILogger, DateTimeOffset, Exception?> _logAppStarted =
            LoggerMessage.Define<DateTimeOffset>(
                LogLevel.Information,
                new EventId(1000, "AppStarted"),
                "Application started at {Local}");

        private static readonly Action<ILogger, string, string, Exception?> _logHttpRequest =
            LoggerMessage.Define<string, string>(
                LogLevel.Debug, // lowered verbosity
                new EventId(1001, "HttpRequest"),
                "HTTP {Method} {Path}");

        private static readonly Action<ILogger, string, string, Exception?> _logLocalizationConfig =
            LoggerMessage.Define<string, string>(
                LogLevel.Information,
                new EventId(1002, "LocalizationConfig"),
                "Localization config: Default={Default}, Supported=[{Supported}]");

        [GeneratedRegex(@"\b\d{6,}\b", RegexOptions.Compiled)]
        private static partial Regex LongDigitsRegex();

        [GeneratedRegex(@"\b[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\b", RegexOptions.Compiled)]
        private static partial Regex GuidRegex();

        // Central lists to keep static asset detection maintainable
        private static readonly string[] StaticPathPrefixes =
        [
            "/css/",
            "/js/",
            "/lib/",
            "/images/",
            "/img/",
            "/favicon",
            "/bootstrap",
            "/bootswatch",
            "/bootstrap-icons",
            "/_framework/"
        ];

        private static readonly string[] StaticFileExtensions =
        [
            ".css", ".js", ".map", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico",
            ".woff", ".woff2", ".ttf", ".eot", ".txt", ".json"
        ];

        private static bool IsStaticAssetPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            // Prefix check for common static folders
            if (path[0] == '/')
            {
                foreach (string prefix in StaticPathPrefixes)
                {
                    if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            // Extension check (only compute once)
            int dot = path.LastIndexOf('.') ;
            if (dot >= 0)
            {
                ReadOnlySpan<char> ext = path.AsSpan(dot);
                foreach (string e in StaticFileExtensions)
                {
                    if (ext.Equals(e, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string RedactPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            string p = path!;
            // Mask long digit sequences (e.g., SSN-like) and GUIDs
            p = LongDigitsRegex().Replace(p, "***");
            p = GuidRegex().Replace(p, "***");
            return p;
        }

        public static WebApplication ConfigureRequestPipeline(this WebApplication app)
        {
            ArgumentNullException.ThrowIfNull(app);

            _ = app.UseResponseCompression();
            _ = app.UseHttpsRedirection();

            // Localization middleware
            RequestLocalizationOptions locOpts = app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
            _ = app.UseRequestLocalization(locOpts);

            // Session for per-user cached data
            _ = app.UseSession();

            // Emit a startup log entry (app kind)
            ILogger startupLogger = app.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("App.Startup");
            _logAppStarted(startupLogger, DateTimeOffset.Now, null);

            // Request logging middleware (reduced verbosity, dev-only, redact potential PII)
            IHostEnvironment envCurrent = app.Services.GetRequiredService<IHostEnvironment>();
            if (envCurrent.IsDevelopment())
            {
                ILogger reqLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("App.Http");
                _ = app.Use(async (ctx, next) =>
                {
                    string method = ctx.Request?.Method ?? string.Empty;
                    string rawPath = ctx.Request?.Path.Value ?? string.Empty;

                    // Skip static assets to reduce noise
                    if (!IsStaticAssetPath(rawPath))
                    {
                        string safePath = RedactPath(rawPath);
                        _logHttpRequest(reqLog, method, safePath, null);
                    }

                    await next();
                });
            }

            // After app.UseRequestLocalization(locOpts);
            ILogger locLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Localization");
            LocalizationConfig locCfg = app.Services.GetRequiredService<IOptions<LocalizationConfig>>().Value;
            string defaultCulture = locCfg.DefaultCulture ?? string.Empty;
            IEnumerable<string> supportedList = locCfg.SupportedCultures ?? Enumerable.Empty<string>();
            string supported = string.Join(", ", supportedList);
            _logLocalizationConfig(locLogger, defaultCulture, supported, null);

            // Diagnostic endpoint to verify applied culture at runtime
            _ = app.MapGet("/__culture", (HttpContext ctx,
                                      IOptions<RequestLocalizationOptions> locOpts,
                                      IOptions<LocalizationConfig> cfg) =>
            {
                IRequestCultureFeature? feature = ctx.Features.Get<IRequestCultureFeature>();
                return Results.Json(new
                {
                    FromConfig = new
                    {
                        cfg.Value.DefaultCulture,
                        cfg.Value.SupportedCultures
                    },
                    Applied = new
                    {
                        Default = locOpts.Value.DefaultRequestCulture.Culture.Name,
                        Supported = (locOpts.Value.SupportedCultures?.Select(c => c.Name).ToArray()) ?? [],
                        Current = feature?.RequestCulture.Culture.Name,
                        CurrentUI = feature?.RequestCulture.UICulture.Name
                    }
                });
            });

            // Static files + routing + pages
            _ = app.UseStaticFiles();
            _ = app.UseRouting();

            // Anti-forgery middleware
            _ = app.UseAntiforgery();

            // Culture switch endpoint with manual antiforgery validation
            _ = app.MapPost("/set-culture", async ([FromForm] string culture, [FromForm] string? returnUrl, HttpContext ctx, Microsoft.AspNetCore.Antiforgery.IAntiforgery af) =>
            {
                await af.ValidateRequestAsync(ctx);

                // Ensure the requested culture is one of the supported UI cultures
                bool supported = locOpts.SupportedUICultures != null &&
                    locOpts.SupportedUICultures.Any(c => string.Equals(c.Name, culture, StringComparison.OrdinalIgnoreCase));
                if (!supported)
                {
                    return Results.BadRequest();
                }

                string cookieValue = CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture));
                ctx.Response.Cookies.Append(
                    CookieRequestCultureProvider.DefaultCookieName,
                    cookieValue,
                    new CookieOptions
                    {
                        Expires = DateTimeOffset.Now.AddYears(1),
                        IsEssential = true,
                        Secure = ctx.Request.IsHttps,
                        SameSite = SameSiteMode.Lax,
                        Path = "/"
                    });

                return Results.LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
            });

            _ = app.MapRazorPages();

            // Logs endpoints (defensive when store/feed not registered) - restrict to Development environment
            if (envCurrent.IsDevelopment())
            {
                _ = app.MapGet("/logs", (HttpContext ctx, [FromQuery] string? kind) =>
                {
                    IHttpLogStore? store = ctx.RequestServices.GetService<IHttpLogStore>();
                    if (store is null)
                    {
                        return Results.Json(new { ok = false, error = "Log store not available" }, statusCode: StatusCodes.Status503ServiceUnavailable);
                    }
                    IReadOnlyList<LogEntry> items = store.GetAll();
                    if (!string.IsNullOrWhiteSpace(kind))
                    {
                        items = [.. items.Where(i => string.Equals(i.Kind, kind, StringComparison.OrdinalIgnoreCase))];
                    }
                    return Results.Json(new { ok = true, items });
                });

                _ = app.MapPost("/logs/clear", (HttpContext ctx) =>
                {
                    IHttpLogStore? store = ctx.RequestServices.GetService<IHttpLogStore>();
                    if (store is null)
                    {
                        return Results.Json(new { ok = false, error = "Log store not available" }, statusCode: StatusCodes.Status503ServiceUnavailable);
                    }
                    store.Clear();
                    return Results.Json(new { ok = true });
                });

                _ = app.MapPost("/logs/write", ([FromBody] LogWriteDto dto, HttpContext ctx) =>
                {
                    if (dto is null)
                    {
                        return Results.BadRequest();
                    }
                    IHttpLogStore? store = ctx.RequestServices.GetService<IHttpLogStore>();
                    if (store is null)
                    {
                        return Results.Json(new { ok = false, error = "Log store not available" }, statusCode: StatusCodes.Status503ServiceUnavailable);
                    }
                    if (!Enum.TryParse(dto.Level ?? "Information", true, out LogLevel lvl))
                    {
                        lvl = LogLevel.Information;
                    }
                    store.Add(new LogEntry
                    {
                        Timestamp = DateTimeOffset.Now,
                        Level = lvl,
                        Category = dto.Category ?? "Manual",
                        EventId = dto.EventId ?? 0,
                        Kind = "app",
                        Message = dto.Message ?? string.Empty,
                        Exception = null
                    });
                    return Results.Json(new { ok = true });
                });

                // Server-Sent Events: real-time log stream (accepts kind filter)
                _ = app.MapGet("/logs/stream", async (HttpContext ctx, [FromQuery] string? kind, CancellationToken ct) =>
                {
                    ILogFeed? stream = ctx.RequestServices.GetService<ILogFeed>();
                    if (stream is null)
                    {
                        return Results.Json(new { ok = false, error = "Log stream not available" }, statusCode: StatusCodes.Status503ServiceUnavailable);
                    }

                    ctx.Response.Headers.CacheControl = "no-cache";
                    ctx.Response.Headers.Connection = "keep-alive";
                    ctx.Response.Headers.Append("X-Accel-Buffering", "no");
                    ctx.Response.ContentType = "text/event-stream";

                    (System.Threading.Channels.ChannelReader<LogEntry> reader, Guid id) = stream.Subscribe();
                    try
                    {
                        await foreach (LogEntry entry in reader.ReadAllAsync(ct))
                        {
                            if (!string.IsNullOrWhiteSpace(kind) && !string.Equals(entry.Kind, kind, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            string json = System.Text.Json.JsonSerializer.Serialize(entry);
                            await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
                            await ctx.Response.Body.FlushAsync(ct);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    finally
                    {
                        stream.Unsubscribe(id);
                    }

                    return Results.Empty;
                });
            }

            // Culture defaults for threads (optional)
            CultureInfo culture = locOpts.DefaultRequestCulture.Culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            return app;
        }
    }
}
