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

            _ = services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddConfiguration(configuration.GetSection("Logging"));
                builder.AddConsole(o => o.IncludeScopes = true);
                builder.AddDebug();
            });

            // OpenTelemetry metrics and tracing
            _ = services.AddOpenTelemetry()
                .ConfigureResource(rb =>
                {
                    rb.AddService(serviceName: "XRoadFolkWeb",
                                  serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0");
                })
                .WithMetrics(metrics =>
                {
                    // Collect our meters
                    metrics.AddMeter("XRoadFolkRaw");
                    metrics.AddMeter("XRoadFolkWeb");

                    // ASP.NET Core and runtime metrics are optional; enable based on configuration if needed
                    bool includeAspNet = configuration.GetValue<bool>("OpenTelemetry:Metrics:AspNetCore", true);
                    bool includeRuntime = configuration.GetValue<bool>("OpenTelemetry:Metrics:Runtime", false);
                    if (includeAspNet) metrics.AddAspNetCoreInstrumentation();
                    if (includeRuntime) metrics.AddRuntimeInstrumentation();

                    // Prometheus scrape endpoint (optional; disabled by default)
                    if (configuration.GetValue<bool>("OpenTelemetry:Exporters:Prometheus:Enabled", false))
                    {
                        metrics.AddPrometheusExporter();
                    }

                    // Console exporter (optional)
                    if (configuration.GetValue<bool>("OpenTelemetry:Exporters:Console:Enabled", false))
                    {
                        metrics.AddConsoleExporter();
                    }

                    // OTLP exporter (optional)
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
                })
                .WithTracing(tracing =>
                {
                    // Basic ASP.NET Core tracing
                    tracing.AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.Filter = httpContext => true;
                    });
                    tracing.AddHttpClientInstrumentation();

                    // Console exporter (optional)
                    if (configuration.GetValue<bool>("OpenTelemetry:Exporters:Console:Enabled", false))
                    {
                        tracing.AddConsoleExporter();
                    }

                    // OTLP exporter (optional)
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
                });

            // Fail-fast guard: prevent excessive verbosity in non-development unless explicitly allowed
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
                }
            });

            // Health checks
            _ = services.AddHealthChecks();

            // HttpLog options + validation
            _ = services.AddOptions<HttpLogOptions>()
                    .BindConfiguration("HttpLogs")
                    .Validate(o => o.Capacity >= 50, "HttpLogs:Capacity must be >= 50")
                    .Validate(o => o.MaxQueue >= 100, "HttpLogs:MaxQueue must be >= 100 for file-backed store")
                    .Validate(o => !o.PersistToFile || !string.IsNullOrWhiteSpace(o.FilePath), "HttpLogs:FilePath must be set when PersistToFile is true")
                    .ValidateOnStart();

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

            _ = services.AddSingleton<IHostedService>(sp =>
            {
                HttpLogOptions opts = sp.GetRequiredService<IOptions<HttpLogOptions>>().Value;
                if (opts.PersistToFile && !string.IsNullOrWhiteSpace(opts.FilePath))
                {
                    return new FileBackedLogWriter(sp.GetRequiredService<FileBackedHttpLogStore>(), sp.GetRequiredService<IOptions<HttpLogOptions>>());
                }
                return new NoopHostedService();
            });

            _ = services.AddSingleton<ILoggerProvider>(sp => new InMemoryHttpLogLoggerProvider(sp.GetRequiredService<IHttpLogStore>()));
            return services;
        }

        private sealed class NoopHostedService : IHostedService
        {
            public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
