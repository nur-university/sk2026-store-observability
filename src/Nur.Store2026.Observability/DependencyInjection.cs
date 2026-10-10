using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nur.Store2026.Observability.Config;
using System.Reflection;
using Nur.Store2026.Observability.Tracing;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Enrichers.Span;

namespace Nur.Store2026.Observability;

public static class DependencyInjection
{
    /// <summary>
    /// Meters que alimentan los dashboards. Los de negocio de cada servicio se publican bajo el
    /// prefijo <c>Store.</c> (p. ej. <c>Store.Orders</c>) y se recogen todos con el comodin.
    /// </summary>
    private static readonly string[] Meters =
    [
        "Joseco.Communication.RabbitMQ",
        "Joseco.Outbox",
        "Npgsql",
        "Store.*"
    ];

    private static readonly string[] FastDurationInstruments =
    [
        "rabbitmq.message.processing.duration",
        "rabbitmq.message.publish.duration",
        "outbox.message.publish.duration"
    ];

    // Tiempos de espera en cola / outbox: pueden ser de minutos si algo se atasca.
    private static readonly string[] SlowDurationInstruments =
    [
        "rabbitmq.message.delivery.latency",
        "outbox.message.delay"
    ];

    // Tiempos de negocio: de segundos (cobro inmediato) a dias (venta pagada sin retirar).
    private static readonly string[] BusinessDurationInstruments =
    [
        "store.orders.lead_time",
        "store.payments.collection_time",
        "store.inventory.sale_pickup_time"
    ];

    private static readonly double[] BusinessDurationBuckets =
        [10, 30, 60, 300, 900, 1800, 3600, 7200, 14400, 28800, 86400, 172800, 604800];

    private static readonly double[] FastDurationBuckets = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30];
    private static readonly double[] SlowDurationBuckets = [0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120, 300, 600];

    /// <param name="jeagerSettings">
    /// Se conserva por compatibilidad con los servicios; ya no se usa: las trazas viajan por OTLP
    /// al collector, que las reenvia a Jaeger.
    /// </param>
    /// <param name="metricsFactory">Meters o instrumentaciones de metricas propias del servicio.</param>
    public static IServiceCollection AddObservability(this IServiceCollection services,
        string serviceName,
        JeagerSettings? jeagerSettings = null,
        Action<TracerProviderBuilder>? instrumentationFactory = null,
        bool shouldIncludeHttpInstrumentation = true,
        Action<MeterProviderBuilder>? metricsFactory = null)
    {
        services.AddScoped<ITracingProvider, TracingProvider>();

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: serviceName,
                    serviceNamespace: "store",
                    serviceVersion: Assembly.GetEntryAssembly()?.GetName().Version?.ToString())
                .AddAttributes(
                [
                    new KeyValuePair<string, object>("deployment.environment", ResolveEnvironmentName())
                ]))
            .WithTracing(builder =>
            {
                builder.AddAspNetCoreInstrumentation();

                if (shouldIncludeHttpInstrumentation)
                {
                    builder.AddHttpClientInstrumentation();
                }

                instrumentationFactory?.Invoke(builder);

                // Destino: OTEL_EXPORTER_OTLP_ENDPOINT (variable de entorno o clave de configuracion,
                // p. ej. http://otel-collector:4317 en Docker); por defecto http://localhost:4317.
                builder.AddOtlpExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation()
                    .AddMeter(Meters);

                if (shouldIncludeHttpInstrumentation)
                {
                    metrics.AddHttpClientInstrumentation();
                }

                // Los histogramas de .NET 8 no declaran buckets: los por defecto del SDK (0..10000)
                // no sirven para duraciones en segundos.
                foreach (string name in FastDurationInstruments)
                {
                    metrics.AddView(name, new ExplicitBucketHistogramConfiguration { Boundaries = FastDurationBuckets });
                }
                foreach (string name in SlowDurationInstruments)
                {
                    metrics.AddView(name, new ExplicitBucketHistogramConfiguration { Boundaries = SlowDurationBuckets });
                }

                foreach (string name in BusinessDurationInstruments)
                {
                    metrics.AddView(name, new ExplicitBucketHistogramConfiguration { Boundaries = BusinessDurationBuckets });
                }

                metricsFactory?.Invoke(metrics);

                // Los WebApi pueden exponer ademas /metrics (UseOpenTelemetryPrometheusScrapingEndpoint);
                // el camino principal es OTLP, que tambien cubre a los WorkerService.
                metrics.AddPrometheusExporter();

                metrics.AddOtlpExporter();
            });

        return services;
    }

    private static string ResolveEnvironmentName() =>
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
        ?? "Production";

    public static IHostBuilder UseLogging(this IHostBuilder hostBuilder, string serviceName, IConfiguration configuration)
    {
        string seqUrl = configuration["Seq:ServerUrl"]!;
        hostBuilder.UseSerilog((context, services, loggerConfiguration) =>
        {
            ConfigureLogs(loggerConfiguration, serviceName, configuration, seqUrl);
        });

        return hostBuilder;
    }

    public static HostApplicationBuilder UseLogging(this HostApplicationBuilder builder, string serviceName, IConfiguration configuration)
    {
        string seqUrl = configuration["Seq:ServerUrl"]!;

        Log.Logger = ConfigureLogs(new LoggerConfiguration(), serviceName, configuration, seqUrl)
            .CreateLogger();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger);

        return builder;
    }

    private static LoggerConfiguration ConfigureLogs(LoggerConfiguration loggerConfiguration, 
        string serviceName, 
        IConfiguration configuration,
        string? seqUrl = null)
    {

        loggerConfiguration
                .ReadFrom.Configuration(configuration)
                .Enrich.FromLogContext()
                .Enrich.WithSpan()
                .Enrich.WithEnvironmentName()
                .Enrich.WithProcessId()
                .Enrich.WithProperty("ServiceName", serviceName)
                .WriteTo.Console();

        if (!string.IsNullOrEmpty(seqUrl))
        {
            loggerConfiguration.WriteTo.Seq(seqUrl);
        }

        return loggerConfiguration;
    }
}
