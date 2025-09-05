using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using XRoadFolkWeb.Infrastructure;
using XRoadFolkWeb.Shared;
using System.IO;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Net.Http.Headers;
using Microsoft.AspNetCore.OutputCaching;
using System.Diagnostics.Metrics;

namespace XRoadFolkWeb.Extensions
{
    public static partial class WebApplicationExtensions
    {
        private const string CorrelationHeader = "X-Correlation-Id";
        private static readonly Meter StartupMeter = new("XRoadFolkWeb.Startup");
        private static readonly Histogram<double> ColdStartSeconds = StartupMeter.CreateHistogram<double>("cold_start_seconds");
        private static readonly Histogram<double> FirstRequestSeconds = StartupMeter.CreateHistogram<double>("first_request_seconds");
        private static readonly long ProcessStartTicks = Stopwatch.GetTimestamp();
        private static bool _firstRequestRecorded;

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

        [GeneratedRegex(@"\b\d{6,}\b")]
        private static partial Regex LongDigitsRegex();

        [GeneratedRegex(@"\b[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\b")]
        private static partial Regex GuidRegex();

        /// <summary>
        /// Central sets to keep static asset detection efficient and maintainable
        /// </summary>
        private static readonly HashSet<string> StaticTopLevelFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            "css", "js", "lib", "images", "img", "bootstrap", "bootswatch", "bootstrap-icons", "_framework"
        };

        private static readonly HashSet<string> StaticFileExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".css", ".js", ".map", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico",
            ".woff", ".woff2", ".ttf", ".eot", ".txt", ".json",
        };

        private static bool IsStaticAssetPath(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            // Fast top-level folder check
            if (path[0] == '/')
            {
                int nextSlash = path.IndexOf('/', 1);
                string firstSegment = nextSlash > 0 ? path.Substring(1, nextSlash - 1) : path[1..];
                if (StaticTopLevelFolders.Contains(firstSegment))
                {
                    return true;
                }
                // Special-case favicon.* which typically lives at the root
                if (firstSegment.StartsWith("favicon", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // Extension check using hash set
            string ext = Path.GetExtension(path);
            if (!string.IsNullOrEmpty(ext) && StaticFileExtensions.Contains(ext))
            {
                return true;
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

            IHostEnvironment env = app.Services.GetRequiredService<IHostEnvironment>();
            IConfiguration configuration = app.Services.GetRequiredService<IConfiguration>();
            ILoggerFactory loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
            ILogger featureLog = loggerFactory.CreateLogger("Features");
            ILogger cultureLog = loggerFactory.CreateLogger("App.Culture");
            bool showDetailedErrors = configuration.GetBoolOrDefault("Features:DetailedErrors", env.IsDevelopment(), featureLog);

            AddCspAndSecurityHeaders(app);
            AddNoCacheHeaders(app);

            // CorrelationId emission & response header
            app.Use(async (ctx, next) =>
            {
                string id = ctx.TraceIdentifier;
                if (string.IsNullOrWhiteSpace(id))
                {
                    id = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
                }
                // mirror as request header if absent (helps downstream middleware/handlers)
                if (!ctx.Request.Headers.ContainsKey(CorrelationHeader))
                {
                    ctx.Request.Headers[CorrelationHeader] = id;
                }
                ctx.Response.OnStarting(() =>
                {
                    if (!ctx.Response.Headers.ContainsKey(CorrelationHeader))
                    {
                        ctx.Response.Headers[CorrelationHeader] = id;
                    }
                    return Task.CompletedTask;
                });
                await next();
            });

            ConfigureExceptionHandling(app, loggerFactory, showDetailedErrors);

            // Friendly status code pages (e.g., 404/403) -> re-execute /Error/{statusCode}
            app.UseStatusCodePagesWithReExecute("/Error/{0}");

            ConfigureTransport(app, env);
            ConfigureLocalization(app);

            // startup log
            ILogger startupLogger = loggerFactory.CreateLogger("App.Startup");
            _logAppStarted(startupLogger, DateTimeOffset.UtcNow, arg3: null);

            ConfigureRequestLoggingOrCompression(app, env, loggerFactory);
            LogLocalization(app, loggerFactory);
            MapDiagnostics(app, env);

            // Health checks endpoints (Kubernetes compatible)
            app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live"),
            });
            app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("ready"),
            });
            // Simple health endpoint
            app.MapGet("/health", () => Results.Text("ok", "text/plain"));

            // static files, routing, session, antiforgery
            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = ctx =>
                {
                    var headers = ctx.Context.Response.Headers;
                    var req = ctx.Context.Request;
                    bool hasVersion = req.Query.ContainsKey("v");
                    if (hasVersion)
                    {
                        headers.CacheControl = "public,max-age=31536000,immutable"; // 1 year
                        headers.Expires = DateTime.UtcNow.AddYears(1).ToString("R");
                    }
                    else
                    {
                        // Short cache for non-versioned assets
                        headers.CacheControl = "public,max-age=3600"; // 1 hour
                    }
                }
            });
            app.UseRouting();

            // First-request JIT timing middleware
            app.Use(async (ctx, next) =>
            {
                bool record = !_firstRequestRecorded && HttpMethods.IsGet(ctx.Request.Method);
                long start = record ? Stopwatch.GetTimestamp() : 0;
                await next();
                if (record)
                {
                    _firstRequestRecorded = true;
                    double sec = (Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency;
                    FirstRequestSeconds.Record(sec);
                }
            });

            // Response caching for safe GETs
            app.UseResponseCaching();
            app.UseOutputCache();

            app.UseCookiePolicy();
            app.UseSession();

            // Enforce antiforgery across endpoints (in addition to MVC filter)
            app.UseAntiforgery();

            // Correlation scope: TraceId, SpanId, User, SessionId
            AddCorrelationScope(app, loggerFactory);

            MapCultureSwitch(app, cultureLog);
            app.MapRazorPages();
            app.MapFallbackToPage("/Index");
            MapLogsEndpoints(app, configuration, env, featureLog);

            // thread culture defaults
            RequestLocalizationOptions locOpts = app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
            RequestCulture defaultReqCulture = locOpts.DefaultRequestCulture;
            CultureInfo.DefaultThreadCurrentCulture = defaultReqCulture.Culture;
            CultureInfo.DefaultThreadCurrentUICulture = defaultReqCulture.UICulture;

            // OpenTelemetry Prometheus scrape endpoint (optional)
            if (configuration.GetValue<bool>("OpenTelemetry:Exporters:Prometheus:Enabled", false))
            {
                app.MapPrometheusScrapingEndpoint();
            }

            return app;
        }

        private static void AddCspAndSecurityHeaders(WebApplication app)
        {
            _ = app.Use(async (ctx, next) =>
            {
                string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
                ctx.Items["CSP_NONCE"] = nonce;

                ctx.Response.OnStarting(() =>
                {
                    IHeaderDictionary headers = ctx.Response.Headers;

                    AddStandardSecurityHeaders(headers);

                    if (!headers.ContainsKey("Content-Security-Policy"))
                    {
                        headers.ContentSecurityPolicy = BuildCsp(nonce);
                    }
                    return Task.CompletedTask;
                });

                await next();
            });
        }

        private static void AddStandardSecurityHeaders(IHeaderDictionary headers)
        {
            if (!headers.ContainsKey("X-Content-Type-Options")) headers.XContentTypeOptions = "nosniff";
            if (!headers.ContainsKey("Referrer-Policy")) headers["Referrer-Policy"] = "no-referrer";
            if (!headers.ContainsKey("X-Frame-Options")) headers.XFrameOptions = "DENY";
            if (!headers.ContainsKey("Permissions-Policy")) headers["Permissions-Policy"] = "accelerometer=(), autoplay=(), camera=(), clipboard-read=(), clipboard-write=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), midi=(), payment=(), usb=(), fullscreen=(), xr-spatial-tracking=(), picture-in-picture=(), publickey-credentials-get=(), screen-wake-lock=(), browsing-topics=()";
            if (!headers.ContainsKey("Cross-Origin-Opener-Policy")) headers["Cross-Origin-Opener-Policy"] = "same-origin";
            if (!headers.ContainsKey("Cross-Origin-Resource-Policy")) headers["Cross-Origin-Resource-Policy"] = "same-origin";
        }

        private static string BuildCsp(string nonce)
        {
            const string JsDelivr = "https://cdn.jsdelivr.net";
            const string GoogleFontsCss = "https://fonts.googleapis.com";
            const string GoogleFontsStatic = "https://fonts.gstatic.com";

            return "default-src 'self'; " +
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

        private static void AddNoCacheHeaders(WebApplication app)
        {
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
        }

        private static void ConfigureExceptionHandling(WebApplication app, ILoggerFactory loggerFactory, bool showDetailedErrors)
        {
            ILogger errorLogger = loggerFactory.CreateLogger("App.Error");
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
                                inner = ex.InnerException is null ? null : new { type = ex.InnerException.GetType().FullName ?? ex.InnerException.GetType().Name, message = ex.InnerException.Message },
                            };
                        }
                        await context.Response.WriteAsJsonAsync(problem);
                    }
                });
            });
        }

        private static void ConfigureTransport(WebApplication app, IHostEnvironment env)
        {
            if (!env.IsDevelopment())
            {
                _ = app.UseHsts();
                _ = app.UseHttpsRedirection();
            }
        }

        private static void ConfigureLocalization(WebApplication app)
        {
            RequestLocalizationOptions locOpts = app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
            _ = app.UseRequestLocalization(locOpts);
        }

        private static void ConfigureRequestLoggingOrCompression(WebApplication app, IHostEnvironment env, ILoggerFactory loggerFactory)
        {
            if (env.IsDevelopment())
            {
                ILogger reqLog = loggerFactory.CreateLogger("App.Http");
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
        }

        private static void LogLocalization(WebApplication app, ILoggerFactory loggerFactory)
        {
            ILogger locLogger = loggerFactory.CreateLogger("Localization");
            LocalizationConfig locCfg = app.Services.GetRequiredService<IOptions<LocalizationConfig>>().Value;
            string defaultCulture = locCfg.DefaultCulture ?? string.Empty;
            IEnumerable<string> supportedList = locCfg.SupportedCultures ?? Enumerable.Empty<string>();
            string supported = string.Join(", ", supportedList);
            _logLocalizationConfig(locLogger, defaultCulture, supported, arg4: null);
        }

        private static void MapDiagnostics(WebApplication app, IHostEnvironment env)
        {
            if (!env.IsDevelopment()) return;

            _ = app.MapGet("/__culture", (HttpContext ctx,
                                          IOptions<RequestLocalizationOptions> locOpts2,
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
                        Default = locOpts2.Value.DefaultRequestCulture.Culture.Name,
                        Supported = (locOpts2.Value.SupportedCultures?.Select(c => c.Name).ToArray()) ?? [],
                        Current = feature?.RequestCulture.Culture.Name,
                        CurrentUI = feature?.RequestCulture.UICulture.Name,
                    },
                });
            });
        }

        private static void MapCultureSwitch(WebApplication app, ILogger cultureLog)
        {
            RequestLocalizationOptions locOpts = app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;

            _ = app.MapPost("/set-culture", async ([FromForm] string culture, [FromForm] string? returnUrl, HttpContext ctx, Microsoft.AspNetCore.Antiforgery.IAntiforgery af) =>
            {
                // Try validate antiforgery using header or form token gracefully
                try
                {
                    await af.ValidateRequestAsync(ctx);
                }
                catch (AntiforgeryValidationException)
                {
                    return Results.BadRequest();
                }

                bool supportedOk = locOpts.SupportedUICultures?.Any(c => string.Equals(c.Name, culture, StringComparison.OrdinalIgnoreCase)) == true;
                if (!supportedOk)
                {
                    return Results.BadRequest();
                }

                string cookieValue = CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture));
                // For TestServer (HTTP), write Set-Cookie header with expected casing for attributes
                if (!ctx.Request.IsHttps)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append(CookieRequestCultureProvider.DefaultCookieName).Append('=').Append(Uri.EscapeDataString(cookieValue));
                    sb.Append("; Expires=").Append(DateTime.UtcNow.AddYears(1).ToString("R"));
                    sb.Append("; Path=/");
                    sb.Append("; SameSite=Lax");
                    sb.Append("; HttpOnly");
                    ctx.Response.Headers.Append(HeaderNames.SetCookie, sb.ToString());
                }
                else
                {
                    ctx.Response.Cookies.Append(
                        CookieRequestCultureProvider.DefaultCookieName,
                        cookieValue,
                        new CookieOptions
                        {
                            Expires = DateTimeOffset.UtcNow.AddYears(1),
                            IsEssential = true,
                            Secure = true,
                            HttpOnly = true,
                            SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                            Path = "/",
                        });
                }

                if (!string.IsNullOrEmpty(returnUrl))
                {
                    try { return Results.LocalRedirect(returnUrl); }
                    catch (Exception ex)
                    {
                        cultureLog.LogWarning(ex, "set-culture: Invalid returnUrl '{ReturnUrl}'. Falling back to '/'.", returnUrl);
                    }
                }
                return Results.LocalRedirect("/");
            }).DisableAntiforgery();
        }

        private static void MapLogsEndpoints(WebApplication app, IConfiguration configuration, IHostEnvironment env, ILogger featureLog)
        {
            bool logsEnabled = configuration.GetBoolOrDefault("Features:Logs:Enabled", env.IsDevelopment(), featureLog);
            if (!logsEnabled) return;

            MapLogsList(app);
            MapLogsClear(app);
            MapLogsWrite(app);
            MapLogsStream(app);
        }

        private static void MapLogsList(WebApplication app)
        {
            _ = app.MapGet("/logs", (HttpContext ctx, [FromQuery] string? kind, [FromQuery] int? page, [FromQuery] int? pageSize) =>
            {
                IHttpLogStore? store = ctx.RequestServices.GetService<IHttpLogStore>();
                if (store is null)
                {
                    return Results.Json(new { ok = false, error = "Log store not available" }, statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                IEnumerable<LogEntry> query = store.GetAll();
                if (!string.IsNullOrWhiteSpace(kind))
                {
                    query = query.Where(i => string.Equals(i.Kind, kind, StringComparison.OrdinalIgnoreCase));
                }

                LogEntry[] all = (query as LogEntry[]) ?? query.ToArray();

                int pg = Math.Max(1, page ?? 1);
                int size = pageSize.HasValue ? Math.Clamp(pageSize.Value, 1, 1000) : 100;
                int total = all.Length;
                int totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)size);
                int skip = (pg - 1) * size;
                LogEntry[] items = (skip >= total) ? Array.Empty<LogEntry>() : all.Skip(skip).Take(size).ToArray();

                return Results.Json(new { ok = true, page = pg, pageSize = size, total, totalPages, items });
            });
        }

        private static void MapLogsClear(WebApplication app)
        {
            _ = app.MapPost("/logs/clear", async (HttpContext ctx, IAntiforgery af) =>
            {
                try
                {
                    await af.ValidateRequestAsync(ctx);
                }
                catch (AntiforgeryValidationException)
                {
                    return Results.BadRequest();
                }

                IHttpLogStore? store = ctx.RequestServices.GetService<IHttpLogStore>();
                if (store is null)
                {
                    return Results.Json(new { ok = false, error = "Log store not available" }, statusCode: StatusCodes.Status503ServiceUnavailable);
                }
                store.Clear();
                return Results.Json(new { ok = true });
            });
        }

        private static void MapLogsWrite(WebApplication app)
        {
            _ = app.MapPost("/logs/write", async ([FromBody] LogWriteDto dto, HttpContext ctx, IAntiforgery af) =>
            {
                try
                {
                    await af.ValidateRequestAsync(ctx);
                }
                catch (AntiforgeryValidationException)
                {
                    return Results.BadRequest();
                }

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
                    Timestamp = DateTimeOffset.UtcNow,
                    Level = lvl,
                    Category = dto.Category ?? "Manual",
                    EventId = dto.EventId ?? 0,
                    Kind = "app",
                    Message = dto.Message ?? string.Empty,
                    Exception = null,
                });
                return Results.Json(new { ok = true });
            });
        }

        private static void MapLogsStream(WebApplication app)
        {
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

        private static void AddCorrelationScope(WebApplication app, ILoggerFactory loggerFactory)
        {
            ILogger scopeLogger = loggerFactory.CreateLogger("App.Correlation");
            app.Use(async (ctx, next) =>
            {
                Activity? activity = Activity.Current;
                string traceId = activity?.TraceId.ToString() ?? string.Empty;
                string spanId = activity?.SpanId.ToString() ?? string.Empty;
                string? user = (ctx.User?.Identity?.IsAuthenticated == true) ? ctx.User!.Identity!.Name : null;
                string? sessionId = null;
                try { sessionId = ctx.Session?.Id; } catch { }

                var scope = new Dictionary<string, object?>();
                if (!string.IsNullOrEmpty(traceId)) scope["TraceId"] = traceId;
                if (!string.IsNullOrEmpty(spanId)) scope["SpanId"] = spanId;
                if (!string.IsNullOrEmpty(user)) scope["User"] = user;
                if (!string.IsNullOrEmpty(sessionId)) scope["SessionId"] = sessionId;

                using (scopeLogger.BeginScope(scope))
                {
                    await next();
                }
            });
        }
    }
}
