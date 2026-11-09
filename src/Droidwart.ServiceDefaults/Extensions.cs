using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Aspire-Standards für Droidwart: OpenTelemetry (Logs, Metriken, Traces) und Resilienz für HttpClients.
/// Angepasst für einen Worker ohne HTTP-Endpunkte (keine ASP.NET-Core-Abhängigkeit, keine Health-Endpunkte).
/// </summary>
public static class Extensions
{
    /// <summary>Name der ActivitySource und des Meters, die Droidwart selbst verwendet.</summary>
    public const string TelemetrySourceName = "Droidwart";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        // Gilt ab Phase 2 für den GitHub-Client: Retries, Timeouts, Circuit Breaker
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

        // Export nur, wenn ein OTLP-Endpunkt gesetzt ist (Aspire setzt ihn automatisch).
        // Im Portainer-Stack bleibt Telemetrie damit ausgeschaltet, solange keiner konfiguriert ist.
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }
}
