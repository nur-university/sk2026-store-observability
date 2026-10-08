using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nur.Store2026.Observability.Config;
using Nur.Store2026.Observability.Tracing;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Enrichers.Span;

namespace Nur.Store2026.Observability;

public static class DependencyInjection
{
    public static IServiceCollection AddObservability(this IServiceCollection services, 
        string serviceName,
        JeagerSettings? jeagerSettings = null, 
        Action<TracerProviderBuilder>? instrumentationFactory = null,
        bool shouldIncludeHttpInstrumentation = true)
    {
        services.AddScoped<ITracingProvider, TracingProvider>();

        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder
                    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName))
                    .AddAspNetCoreInstrumentation();

                if (shouldIncludeHttpInstrumentation)
                {
                    builder.AddHttpClientInstrumentation();
                }

                instrumentationFactory?.Invoke(builder);

                if (jeagerSettings != null)
                {
                    builder.AddJaegerExporter(options =>
                    {
                        options.AgentHost = jeagerSettings.Host;
                        options.AgentPort = jeagerSettings.Port;
                    });
                }

                //builder.AddOtlpExporter(config =>
                //{
                //    config.Endpoint = new Uri($"{otlpExporterSettings?.Host}:{otlpExporterSettings?.Port}");
                //    config.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                //});
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName)) 
                    .AddMeter("Joseco.Communication.RabbitMQ") 
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation()
                    .AddPrometheusExporter(); 
            });

        return services;
    }

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
