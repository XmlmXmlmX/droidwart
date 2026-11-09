using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Aspire defaults for Droidwart: OpenTelemetry (logs, metrics, traces) and resilience for HttpClients.
/// Adapted for a worker without HTTP endpoints (no ASP.NET Core dependency, no health endpoints).
/// </summary>
public static class Extensions
{
    /// <summary>Name of the ActivitySource and Meter used by Droidwart itself.</summary>
    public const string TelemetrySourceName = "Droidwart";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        // Applies to the GitHub client from phase 2: retries, timeouts, circuit breaker
        builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(TelemetrySourceName)
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                .AddSource(TelemetrySourceName)
                .AddHttpClientInstrumentation());

        // Export only if an OTLP endpoint is set (Aspire sets it automatically).
        // In the Portainer stack, telemetry stays off until one is configured.
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }
}
