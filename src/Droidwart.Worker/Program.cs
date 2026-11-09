using Droidwart.Worker.Adb;
using Droidwart.Worker.Configuration;
using Droidwart.Worker.Inventory;
using Droidwart.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

// OpenTelemetry & Resilienz (Aspire ServiceDefaults). Export nur, wenn OTEL_EXPORTER_OTLP_ENDPOINT gesetzt ist.
builder.AddServiceDefaults();

builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    options.IncludeScopes = false;
});

builder.Services.AddDroidwartOptions();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAdbGateway, AdbGateway>();
builder.Services.AddSingleton<InventoryCollector>();
builder.Services.AddSingleton<InventoryReporter>();
builder.Services.AddHostedService<InventoryWorker>();

var host = builder.Build();
await host.RunAsync();
