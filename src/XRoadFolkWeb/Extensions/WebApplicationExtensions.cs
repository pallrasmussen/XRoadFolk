using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using XRoadFolkWeb.Infrastructure;

namespace XRoadFolkWeb.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication ConfigureRequestPipeline(this WebApplication app)
    {
        app.UseResponseCompression();

        // Redirect to HTTPS in dev so secure cookies work
        app.UseHttpsRedirection();

        // Localization middleware
        RequestLocalizationOptions locOpts = app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
        app.UseRequestLocalization(locOpts);

        // After app.UseRequestLocalization(locOpts);
        var locLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Localization");
        var locCfg = app.Services.GetRequiredService<IOptions<LocalizationConfig>>().Value;
        locLogger.LogInformation("Localization config: Default={Default}, Supported=[{Supported}]",
            locCfg.DefaultCulture, string.Join(", ", locCfg.SupportedCultures));

        // Diagnostic endpoint to verify applied culture at runtime
        app.MapGet("/__culture", (HttpContext ctx,
                                  IOptions<RequestLocalizationOptions> locOpts,
                                  IOptions<LocalizationConfig> cfg) =>
        {
            var feature = ctx.Features.Get<Microsoft.AspNetCore.Localization.IRequestCultureFeature>();
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
                    Supported = locOpts.Value.SupportedCultures.Select(c => c.Name).ToArray(),
                    Current = feature?.RequestCulture.Culture.Name,
                    CurrentUI = feature?.RequestCulture.UICulture.Name
                }
            });
        });

        // Static files + routing + pages
        app.UseStaticFiles();
        app.UseRouting();

        // Anti-forgery middleware
        app.UseAntiforgery();

        // Culture switch endpoint with manual antiforgery validation
        app.MapPost("/set-culture", async ([FromForm] string culture, [FromForm] string? returnUrl, HttpContext ctx, Microsoft.AspNetCore.Antiforgery.IAntiforgery af) =>
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
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    IsEssential = true,
                    Secure = ctx.Request.IsHttps, // allow in dev over HTTP
                    SameSite = SameSiteMode.Lax,
                    Path = "/"
                });

            return Results.LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
        });

        app.MapRazorPages();

        // Logs endpoints (generic with kind=http|soap|app)
        app.MapGet("/logs", ([FromQuery] string? kind, IHttpLogStore store) =>
        {
            var items = store.GetAll();
            if (!string.IsNullOrWhiteSpace(kind))
            {
                items = items.Where(i => string.Equals(i.Kind, kind, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            return Results.Json(new { ok = true, items });
        });
        app.MapPost("/logs/clear", (IHttpLogStore store) => { store.Clear(); return Results.Json(new { ok = true }); });
        app.MapPost("/logs/write", ([FromBody] XRoadFolkWeb.LogWriteDto dto, IHttpLogStore store) =>
        {
            if (dto is null) return Results.BadRequest();
            if (!Enum.TryParse<LogLevel>(dto.Level ?? "Information", true, out var lvl)) lvl = LogLevel.Information;
            store.Add(new XRoadFolkWeb.Infrastructure.LogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
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
        app.MapGet("/logs/stream", async (HttpContext ctx, [FromQuery] string? kind, ILogStream stream, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";
            ctx.Response.Headers.Add("X-Accel-Buffering", "no"); // for proxies like nginx
            ctx.Response.ContentType = "text/event-stream";

            var (reader, id) = stream.Subscribe();
            try
            {
                await foreach (var entry in reader.ReadAllAsync(ct))
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
            catch (OperationCanceledException) { }
            finally
            {
                stream.Unsubscribe(id);
            }
        });

        // Culture defaults for threads (optional)
        CultureInfo culture = locOpts.DefaultRequestCulture.Culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        return app;
    }
}
