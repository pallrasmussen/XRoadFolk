using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using XRoadFolkWeb.Infrastructure;
using XRoadFolkWeb.Shared;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Diagnostics;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Net;

namespace XRoadFolkWeb.Extensions
{
    public static partial class WebApplicationExtensions
    {
        /// <summary>
        /// LoggerMessage delegates for performance
        /// </summary>
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

        private static readonly Action<ILogger, string?, string?, Exception?> _logUnhandledException =
            LoggerMessage.Define<string?, string?>(
                LogLevel.Error,
                new EventId(1003, "UnhandledException"),
                "Unhandled exception at {Path}. TraceId={TraceId}");

        [GeneratedRegex(@"\b\d{6,}\b", RegexOptions.Compiled)]
        private static partial Regex LongDigitsRegex();

        [GeneratedRegex(@"\b[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\b", RegexOptions.Compiled)]
        private static partial Regex GuidRegex();

        /// <summary>
        /// Central lists to keep static asset detection maintainable
        /// </summary>
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
            "/_framework/",
        ];

        private static readonly string[] StaticFileExtensions =
        [
            ".css", ".js", ".map", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico",
            ".woff", ".woff2", ".ttf", ".eot", ".txt", ".json",
        ];

        private static bool IsStaticAssetPath(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

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
            int dot = path.LastIndexOf('.');
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
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string p = path!;
            // Mask long digit sequences (e.g., SSN-like) and GUIDs
            p = LongDigitsRegex().Replace(p, "***");
            p = GuidRegex().Replace(p, "***");
            return p;
        }

        public static WebApplication ConfigureRequestPipeline(this WebApplication app)
        {
            ArgumentNullException.ThrowIfNull(app);

            // Resolve environment + configuration early
            IHostEnvironment hostEnv = app.Services.GetRequiredService<IHostEnvironment>();
            IConfiguration configuration = app.Services.GetRequiredService<IConfiguration>();
            bool showDetailedErrors = configuration.GetValue<bool?>("Features:DetailedErrors") ?? hostEnv.IsDevelopment();

            // CSP + security headers middleware with per-request nonce
            _ = app.Use(async (ctx, next) =>
            {
                string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
                ctx.Items["CSP_NONCE"] = nonce;

                ctx.Response.OnStarting(() =>
                {
                    IHeaderDictionary headers = ctx.Response.Headers;

                    // Standard security headers
                    if (!headers.ContainsKey("X-Content-Type-Options"))
                    {
                        headers.XContentTypeOptions = "nosniff";
                    }
                    if (!headers.ContainsKey("Referrer-Policy"))
                    {
                        headers["Referrer-Policy"] = "no-referrer";
                    }
                    if (!headers.ContainsKey("X-Frame-Options"))
                    {
                        headers.XFrameOptions = "DENY";
                    }
                    if (!headers.ContainsKey("Permissions-Policy"))
                    {
                        headers["Permissions-Policy"] = "accelerometer=(), autoplay=(), camera=(), clipboard-read=(), clipboard-write=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), midi=(), payment=(), usb=(), fullscreen=(), xr-spatial-tracking=(), picture-in-picture=(), publickey-credentials-get=(), screen-wake-lock=(), browsing-topics=()";
                    }
                    if (!headers.ContainsKey("Cross-Origin-Opener-Policy"))
                    {
                        headers["Cross-Origin-Opener-Policy"] = "same-origin";
                    }
                    if (!headers.ContainsKey("Cross-Origin-Resource-Policy"))
                    {
                        headers["Cross-Origin-Resource-Policy"] = "same-origin";
                    }

                    // CSP header
                    if (!headers.ContainsKey("Content-Security-Policy"))
                    {
                        const string JsDelivr = "https://cdn.jsdelivr.net";
                        const string GoogleFontsCss = "https://fonts.googleapis.com";
                        const string GoogleFontsStatic = "https://fonts.gstatic.com";

                        headers.ContentSecurityPolicy = "default-src 'self'; " +
                                     "base-uri 'self'; " +
                                     "frame-ancestors 'none'; " +
                                     "object-src 'none'; " +
                                     $"img-src 'self' data: {JsDelivr}; " +
                                     $"font-src 'self' data: {JsDelivr} {GoogleFontsStatic}; " +
                                     $"script-src 'self' 'nonce-{nonce}'; " +
                                     $"script-src-elem 'self' 'nonce-{nonce}'; " +
                                     $"style-src 'self' 'nonce-{nonce}' {JsDelivr} {GoogleFontsCss}; " +
                                     $"style-src-elem 'self' 'nonce-{nonce}' {JsDelivr} {GoogleFontsCss}; " +
                                     "style-src-attr 'none'; " +
                                     "connect-src 'self'; " +
                                     "form-action 'self'; " +
                                     "upgrade-insecure-requests";
                    }
                    return Task.CompletedTask;
                });

                await next();
            });

            // Disable caching of search results (Index page and related JSON endpoints)
            _ = app.Use(async (ctx, next) =>
            {
                PathString p = ctx.Request.Path;
                if (p.Equals("/", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals("/Index", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals("/Index/PersonDetails", StringComparison.OrdinalIgnoreCase))
                {
                    // Set headers before the response starts
                    ctx.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                    ctx.Response.Headers.Pragma = "no-cache";
                    ctx.Response.Headers.Expires = "0";
                }

                await next();
            });

            // Global exception handling (with optional detailed output)
            ILogger errorLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("App.Error");
            _ = app.UseExceptionHandler(errorApp =>
            {
                errorApp.Run(async context =>
                {
                    IExceptionHandlerPathFeature? feature = context.Features.Get<IExceptionHandlerPathFeature>();
                    string? traceId = Activity.Current?.Id ?? context.TraceIdentifier;
                    _logUnhandledException(errorLogger, feature?.Path, traceId, feature?.Error);

                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;

                    Exception? ex = feature?.Error;
                    string title = showDetailedErrors && ex is not null ? ex.Message : "An unexpected error occurred.";

                    string accept = context.Request.Headers.Accept.ToString();
                    if (!string.IsNullOrWhiteSpace(accept) && accept.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.ContentType = "text/html; charset=utf-8";
                        if (showDetailedErrors && ex is not null)
                        {
                            string msg = WebUtility.HtmlEncode(ex.Message);
                            string type = WebUtility.HtmlEncode(ex.GetType().FullName ?? ex.GetType().Name);
                            string stack = WebUtility.HtmlEncode(ex.StackTrace ?? "");
                            string html = $"<!doctype html><html><head><title>Error</title><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"></head><body><h1>{msg}</h1><p><strong>Type:</strong> {type}</p><p><strong>Trace Id:</strong> {traceId}</p><pre style=\"white-space:pre-wrap;\">{stack}</pre></body></html>";
                            await context.Response.WriteAsync(html);
                        }
                        else
                        {
                            await context.Response.WriteAsync("<!doctype html><html><head><title>Error</title></head><body><h1>An unexpected error occurred.</h1><p>Trace Id: " + traceId + "</p></body></html>");
                        }
                    }
                    else
                    {
                        ProblemDetails problem = new()
                        {
                            Status = StatusCodes.Status500InternalServerError,
                            Title = title,
                            Type = "about:blank",
                            Instance = context.Request?.Path,
                        };
                        problem.Extensions["traceId"] = traceId;
                        if (showDetailedErrors && ex is not null)
                        {
                            problem.Extensions["exception"] = new
                            {
                                type = ex.GetType().FullName ?? ex.GetType().Name,
                                message = ex.Message,
                                stackTrace = ex.StackTrace,
                                inner = ex.InnerException is null ? null : new { type = ex.InnerException.GetType().FullName ?? ex.InnerException.GetType().Name, message = ex.InnerException.Message }
                            };
                        }
                        await context.Response.WriteAsJsonAsync(problem);
                    }
                });
            });

            // HTTPS + HSTS only outside Development
            IHostEnvironment envForHttps = app.Services.GetRequiredService<IHostEnvironment>();
            if (!envForHttps.IsDevelopment())
            {
                _ = app.UseHsts();
                _ = app.UseHttpsRedirection();
            }

            // Localization middleware
            RequestLocalizationOptions locOpts = app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
            _ = app.UseRequestLocalization(locOpts);

            // Emit a startup log entry (app kind)
            ILogger startupLogger = app.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("App.Startup");
            _logAppStarted(startupLogger, DateTimeOffset.Now, arg3: null);

            // Request logging middleware (reduced verbosity, dev-only, redact potential PII) and conditional compression
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
                        _logHttpRequest(reqLog, method, safePath, arg4: null);
                    }

                    await next();
                });
            }
            else
            {
                // Enable compression only outside Development to avoid interfering with browser refresh
                _ = app.UseResponseCompression();
            }

            // After app.UseRequestLocalization(locOpts);
            ILogger locLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Localization");
            LocalizationConfig locCfg = app.Services.GetRequiredService<IOptions<LocalizationConfig>>().Value;
            string defaultCulture = locCfg.DefaultCulture ?? string.Empty;
            IEnumerable<string> supportedList = locCfg.SupportedCultures ?? Enumerable.Empty<string>();
            string supported = string.Join(", ", supportedList);
            _logLocalizationConfig(locLogger, defaultCulture, supported, arg4: null);

            // Diagnostic endpoint to verify applied culture at runtime (Development only)
            if (app.Services.GetRequiredService<IHostEnvironment>().IsDevelopment())
            {
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
                            cfg.Value.SupportedCultures,
                        },
                        Applied = new
                        {
                            Default = locOpts.Value.DefaultRequestCulture.Culture.Name,
                            Supported = (locOpts.Value.SupportedCultures?.Select(c => c.Name).ToArray()) ?? [],
                            Current = feature?.RequestCulture.Culture.Name,
                            CurrentUI = feature?.RequestCulture.UICulture.Name,
                        }
                    });
                });
            }

            // Static files + routing + pages
            _ = app.UseStaticFiles();
            _ = app.UseRouting();

            // Move session here to avoid running it for static files
            _ = app.UseSession();

            // Anti-forgery middleware
            _ = app.UseAntiforgery();

            // Culture switch endpoint with framework validation via LocalRedirect
            _ = app.MapPost("/set-culture", async ([FromForm] string culture, [FromForm] string? returnUrl, HttpContext ctx, Microsoft.AspNetCore.Antiforgery.IAntiforgery af) =>
            {
                await af.ValidateRequestAsync(ctx);

                // Validate requested culture
                IOptions<RequestLocalizationOptions> locOpts = ctx.RequestServices.GetRequiredService<IOptions<RequestLocalizationOptions>>();
                bool supported = locOpts.Value.SupportedUICultures?.Any(c => string.Equals(c.Name, culture, StringComparison.OrdinalIgnoreCase)) == true;
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
                        Secure = ctx.Request.IsHttps,     // allow HTTP in dev
                        HttpOnly = true,
                        SameSite = SameSiteMode.Lax,      // send on top-level redirect
                        Path = "/",
                    });

                // Let LocalRedirect perform framework validation; if invalid, fallback to '/'
                if (!string.IsNullOrEmpty(returnUrl))
                {
                    try { return Results.LocalRedirect(returnUrl); } catch { }
                }
                return Results.LocalRedirect("/");
            });

            _ = app.MapRazorPages();
            _ = app.MapFallbackToPage("/Index");

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
                    if (!Enum.TryParse(dto.Level ?? "Information", ignoreCase: true, out LogLevel lvl))
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
                        Exception = null,
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
