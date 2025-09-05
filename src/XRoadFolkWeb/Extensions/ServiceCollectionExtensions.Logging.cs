using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
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

            ConfigureBuiltInLogging(services, configuration);
            AddOpenTelemetryPipelines(services, configuration);
            PostConfigureLoggerFilters(services, configuration);
            _ = services.AddHealthChecks();
            ConfigureHttpLogOptions(services);
            RegisterHttpLogServices(services);
            RegisterHostedWriters(services);
            RegisterLogProvider(services);
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

            bool includeAspNet = configuration.GetValue<bool>("OpenTelemetry:Metrics:AspNetCore", true);
            bool includeRuntime = configuration.GetValue<bool>("OpenTelemetry:Metrics:Runtime", false);
            if (includeAspNet)
            {
                metrics.AddAspNetCoreInstrumentation();
            }
            if (includeRuntime)
            {
                metrics.AddRuntimeInstrumentation();
            }

            if (configuration.GetValue<bool>("OpenTelemetry:Exporters:Prometheus:Enabled", false))
            {
                metrics.AddPrometheusExporter();
            }
            if (configuration.GetValue<bool>("OpenTelemetry:Exporters:Console:Enabled", false))
            {
                metrics.AddConsoleExporter();
            }
            if (configuration.GetValue<bool>("OpenTelemetry:Exporters:Otlp:Enabled", false))
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

            if (configuration.GetValue<bool>("OpenTelemetry:Exporters:Console:Enabled", false))
            {
                tracing.AddConsoleExporter();
            }
            if (configuration.GetValue<bool>("OpenTelemetry:Exporters:Otlp:Enabled", false))
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
                    bool verbose = configuration.GetValue<bool>("Logging:Verbose", false);
                    bool allowVerbose = configuration.GetValue<bool>("Logging:AllowVerboseInProduction", false);
                    if (verbose && !allowVerbose)
                    {
                        configuration["Logging:Verbose"] = "false";
                    }

                    void Upsert(string? provider, string category, LogLevel level)
                    {
                        int found = -1;
                        for (int i = 0; i < opts.Rules.Count; i++)
                        {
                            var r = opts.Rules[i];
                            if (string.Equals(r.ProviderName, provider, StringComparison.Ordinal) && string.Equals(r.CategoryName, category, StringComparison.Ordinal))
                            {
                                found = i;
                                break;
                            }
                        }
                        var rule = new LoggerFilterRule(providerName: provider, categoryName: category, logLevel: level, filter: null);
                        if (found >= 0)
                        {
                            opts.Rules[found] = rule;
                        }
                        else
                        {
                            opts.Rules.Add(rule);
                        }
                    }

                    Upsert(null, "Microsoft", LogLevel.Warning);
                    Upsert(null, "Microsoft.AspNetCore", LogLevel.Warning);
                    Upsert(null, "System", LogLevel.Warning);
                    Upsert(null, "Microsoft.Hosting.Lifetime", LogLevel.Information);
                    Upsert(null, "XRoadFolkWeb", LogLevel.Information);
                    Upsert(null, "XRoadFolkRaw", LogLevel.Information);
                }
            });
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
            _ = services.AddSingleton<ILogFeed, LogStreamBroadcaster>();
            _ = services.AddSingleton<FileBackedHttpLogStore>();

            _ = services.AddSingleton<IHttpLogStore>(sp =>
            {
                HttpLogOptions opts = sp.GetRequiredService<IOptions<HttpLogOptions>>().Value;
                if (opts.PersistToFile && !string.IsNullOrWhiteSpace(opts.FilePath))
                {
                    return sp.GetRequiredService<FileBackedHttpLogStore>();
                }
                return new InMemoryHttpLog(sp.GetRequiredService<IOptions<HttpLogOptions>>());
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
            _ = services.AddSingleton<ILoggerProvider>(sp => new InMemoryHttpLogLoggerProvider(sp.GetRequiredService<IHttpLogStore>()));
        }

        private sealed class NoopHostedService : IHostedService
        {
            public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
