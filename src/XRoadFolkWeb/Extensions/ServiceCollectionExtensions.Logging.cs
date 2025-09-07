using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using XRoadFolkWeb.Infrastructure;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace XRoadFolkWeb.Extensions
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAppLogging(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            // Bind app-level logging options (e.g., MaskTokens)
            _ = services.AddOptions<LoggingOptions>()
                        .Bind(configuration.GetSection("Logging"))
                        .ValidateOnStart();

            ConfigureBuiltInLogging(services, configuration);
            AddOpenTelemetryPipelines(services, configuration);
            PostConfigureLoggerFilters(services, configuration);
            _ = services.AddHealthChecks();
            ConfigureHttpLogOptions(services);
            RegisterHttpLogServices(services);
            RegisterHostedWriters(services);
            RegisterLogProvider(services);
            // Validate file persistence path on startup (no-op if not enabled)
            _ = services.AddHostedService<HttpLogStartupValidator>();
            return services;
        }

        private static void ConfigureBuiltInLogging(IServiceCollection services, IConfiguration configuration)
        {
            _ = services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddConfiguration(configuration.GetSection("Logging"));
                builder.AddSimpleConsole(o => o.IncludeScopes = true);
                builder.AddDebug();
            });
        }

        private static void AddOpenTelemetryPipelines(IServiceCollection services, IConfiguration configuration)
        {
            _ = services.AddOpenTelemetry()
                .ConfigureResource(rb => ConfigureResource(rb))
                .WithMetrics(metrics => ConfigureMetrics(metrics, configuration))
                .WithTracing(tracing => ConfigureTracing(tracing, configuration));
        }

        private static void ConfigureResource(ResourceBuilder rb)
        {
            rb.AddService(serviceName: "XRoadFolkWeb",
                          serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0");
        }

        private static void ConfigureMetrics(MeterProviderBuilder metrics, IConfiguration configuration)
        {
            metrics.AddMeter("XRoadFolkRaw");
            metrics.AddMeter("XRoadFolkWeb");
            metrics.AddMeter("XRoadFolkWeb.Startup");

            bool includeAspNet = configuration.GetValue<bool>(key: "OpenTelemetry:Metrics:AspNetCore", defaultValue: true);
            bool includeRuntime = configuration.GetValue<bool>(key: "OpenTelemetry:Metrics:Runtime", defaultValue: false);
            if (includeAspNet)
            {
                metrics.AddAspNetCoreInstrumentation();
            }
            if (includeRuntime)
            {
                metrics.AddRuntimeInstrumentation();
            }

            if (configuration.GetValue<bool>(key: "OpenTelemetry:Exporters:Prometheus:Enabled", defaultValue: false))
            {
                metrics.AddPrometheusExporter();
            }
            if (configuration.GetValue<bool>(key: "OpenTelemetry:Exporters:Console:Enabled", defaultValue: false))
            {
                metrics.AddConsoleExporter();
            }
            if (configuration.GetValue<bool>(key: "OpenTelemetry:Exporters:Otlp:Enabled", defaultValue: false))
            {
                string? endpoint = configuration.GetValue<string>("OpenTelemetry:Exporters:Otlp:Endpoint");
                metrics.AddOtlpExporter(o =>
                {
                    if (!string.IsNullOrWhiteSpace(endpoint))
                    {
                        o.Endpoint = new Uri(endpoint);
                    }
                    string? protocol = configuration.GetValue<string>("OpenTelemetry:Exporters:Otlp:Protocol");
                    if (string.Equals(protocol, "grpc", StringComparison.OrdinalIgnoreCase))
                    {
                        o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                    }
                    else if (string.Equals(protocol, "http/protobuf", StringComparison.OrdinalIgnoreCase) || string.Equals(protocol, "http", StringComparison.OrdinalIgnoreCase))
                    {
                        o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                    }
                });
            }
        }

        private static void ConfigureTracing(TracerProviderBuilder tracing, IConfiguration configuration)
        {
            tracing.AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = true;
                options.Filter = httpContext => true;
            });
            tracing.AddHttpClientInstrumentation();

            if (configuration.GetValue<bool>(key: "OpenTelemetry:Exporters:Console:Enabled", defaultValue: false))
            {
                tracing.AddConsoleExporter();
            }
            if (configuration.GetValue<bool>(key: "OpenTelemetry:Exporters:Otlp:Enabled", defaultValue: false))
            {
                string? endpoint = configuration.GetValue<string>("OpenTelemetry:Exporters:Otlp:Endpoint");
                tracing.AddOtlpExporter(o =>
                {
                    if (!string.IsNullOrWhiteSpace(endpoint))
                    {
                        o.Endpoint = new Uri(endpoint);
                    }
                    string? protocol = configuration.GetValue<string>("OpenTelemetry:Exporters:Otlp:Protocol");
                    if (string.Equals(protocol, "grpc", StringComparison.OrdinalIgnoreCase))
                    {
                        o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                    }
                    else if (string.Equals(protocol, "http/protobuf", StringComparison.OrdinalIgnoreCase) || string.Equals(protocol, "http", StringComparison.OrdinalIgnoreCase))
                    {
                        o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                    }
                });
            }
        }

        private static void PostConfigureLoggerFilters(IServiceCollection services, IConfiguration configuration)
        {
            _ = services.AddOptions<LoggerFilterOptions>().PostConfigure<IHostEnvironment>((opts, env) =>
            {
                if (!env.IsDevelopment())
                {
                    ApplyProductionVerbosityGuard(configuration);
                    UpsertDefaultRules(opts);
                }
            });
        }

        private static void ApplyProductionVerbosityGuard(IConfiguration configuration)
        {
            bool verbose = configuration.GetValue<bool>(key: "Logging:Verbose", defaultValue: false);
            bool allowVerbose = configuration.GetValue<bool>(key: "Logging:AllowVerboseInProduction", defaultValue: false);
            if (verbose && !allowVerbose)
            {
                configuration["Logging:Verbose"] = "false";
            }
        }

        private static string NormalizeCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                return category;
            }
            string c = category.Trim();
            if (c.EndsWith(".*", StringComparison.Ordinal))
            {
                c = c[..^2];
            }
            if (c.EndsWith(".", StringComparison.Ordinal))
            {
                c = c.TrimEnd('.');
            }
            return c;
        }

        private static void UpsertDefaultRules(LoggerFilterOptions opts)
        {
            Upsert(opts, provider: null, category: "Microsoft", level: LogLevel.Warning);
            Upsert(opts, provider: null, category: "Microsoft.AspNetCore", level: LogLevel.Warning);
            Upsert(opts, provider: null, category: "System", level: LogLevel.Warning);
            Upsert(opts, provider: null, category: "Microsoft.Hosting.Lifetime", level: LogLevel.Information);
            Upsert(opts, provider: null, category: "XRoadFolkWeb", level: LogLevel.Information);
            Upsert(opts, provider: null, category: "XRoadFolkRaw", level: LogLevel.Information);
        }

        private static void Upsert(LoggerFilterOptions opts, string? provider, string category, LogLevel level)
        {
            string norm = NormalizeCategory(category);
            int found = -1;
            for (int i = 0; i < opts.Rules.Count; i++)
            {
                var r = opts.Rules[i];
                string existing = r.CategoryName ?? string.Empty;
                string existingNorm = NormalizeCategory(existing);
                if (string.Equals(r.ProviderName, provider, StringComparison.Ordinal)
                    && string.Equals(existingNorm, norm, StringComparison.Ordinal))
                {
                    found = i;
                    break;
                }
            }
            var rule = new LoggerFilterRule(providerName: provider, categoryName: norm, logLevel: level, filter: null);
            if (found >= 0)
            {
                opts.Rules[found] = rule;
            }
            else
            {
                opts.Rules.Add(rule);
            }
        }

        private static void ConfigureHttpLogOptions(IServiceCollection services)
        {
            _ = services.AddOptions<HttpLogOptions>()
                .BindConfiguration("HttpLogs")
                .Validate(o => o.Capacity >= 50, "HttpLogs:Capacity must be >= 50")
                .Validate(o => o.MaxQueue >= 100, "HttpLogs:MaxQueue must be >= 100 for file-backed store")
                .Validate(o => !o.PersistToFile || !string.IsNullOrWhiteSpace(o.FilePath), "HttpLogs:FilePath must be set when PersistToFile is true")
                .ValidateOnStart();
        }

        private static void RegisterHttpLogServices(IServiceCollection services)
        {
            // IMPORTANT: Avoid resolving ILogger while configuring the logging provider to prevent cycles
            _ = services.AddSingleton<ILogFeed>(sp => new LogStreamBroadcaster(new NullLogger<LogStreamBroadcaster>()));
            _ = services.AddSingleton<FileBackedHttpLogStore>();

            _ = services.AddSingleton<IHttpLogStore>(sp =>
            {
                HttpLogOptions opts = sp.GetRequiredService<IOptions<HttpLogOptions>>().Value;
                IHostEnvironment env = sp.GetRequiredService<IHostEnvironment>();
                // Important: do not resolve ILogger here to avoid circular dependency with our provider
                IHttpLogStore inner = (opts.PersistToFile && !string.IsNullOrWhiteSpace(opts.FilePath))
                    ? sp.GetRequiredService<FileBackedHttpLogStore>()
                    : new InMemoryHttpLog(sp.GetRequiredService<IOptions<HttpLogOptions>>());

                return new MaskingHttpLogStore(inner, sp.GetRequiredService<IOptions<LoggingOptions>>(), env);
            });
        }

        private static void RegisterHostedWriters(IServiceCollection services)
        {
            _ = services.AddSingleton<IHostedService>(sp =>
            {
                HttpLogOptions opts = sp.GetRequiredService<IOptions<HttpLogOptions>>().Value;
                if (opts.PersistToFile && !string.IsNullOrWhiteSpace(opts.FilePath))
                {
                    return new FileBackedLogWriter(sp.GetRequiredService<FileBackedHttpLogStore>(), sp.GetRequiredService<IOptions<HttpLogOptions>>());
                }
                return new NoopHostedService();
            });
        }

        private static void RegisterLogProvider(IServiceCollection services)
        {
            _ = services.AddSingleton<ILoggerProvider>(sp => new InMemoryHttpLogLoggerProvider(
                sp.GetRequiredService<IHttpLogStore>(),
                sp.GetRequiredService<ILogFeed>()));
        }

        private sealed class NoopHostedService : IHostedService
        {
            public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
