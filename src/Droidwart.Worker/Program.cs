using Droidwart.Worker.Adb;
using System.Net.Http.Headers;
using Droidwart.Worker.Configuration;
using Droidwart.Worker.GitHub;
using Droidwart.Worker.Installs;
using Droidwart.Worker.Inventory;
using Droidwart.Worker.Mqtt;
using Droidwart.Worker.Services;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// OpenTelemetry and resilience (Aspire ServiceDefaults). Exports only if OTEL_EXPORTER_OTLP_ENDPOINT is set.
builder.AddServiceDefaults();

builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    options.IncludeScopes = false;
});

builder.Services.AddDroidwartOptions();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAdbGateway, AdbGateway>();

builder.Services.AddHttpClient(GitHubReleaseClient.HttpClientName, (services, client) =>
{
    var options = services.GetRequiredService<IOptions<GitHubOptions>>().Value;
    client.BaseAddress = new Uri(options.ApiBaseUrl);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Droidwart (+https://github.com/XmlmXmlmX/droidwart)");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    if (!string.IsNullOrWhiteSpace(options.Token))
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
    }
})
.AddStandardResilienceHandler();
builder.Services.AddSingleton<GitHubReleaseClient>();

// APK downloads can take minutes, so this client has no resilience handler (its timeouts are far too short).
builder.Services.AddHttpClient(ApkDownloader.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Droidwart (+https://github.com/XmlmXmlmX/droidwart)");
});
builder.Services.AddSingleton<ApkDownloader>();
builder.Services.AddSingleton<InstallQueue>();
builder.Services.AddSingleton<InventoryStore>();
builder.Services.AddSingleton<InventoryCollector>();
builder.Services.AddSingleton<InventoryReporter>();
builder.Services.AddSingleton<InventoryTrigger>();

// MQTT connection is a singleton and a hosted service at the same time (reconnect loop).
builder.Services.AddSingleton<MqttConnection>();
builder.Services.AddHostedService(services => services.GetRequiredService<MqttConnection>());
builder.Services.AddSingleton<HomeAssistantPublisher>();

builder.Services.AddHostedService<InventoryWorker>();
builder.Services.AddHostedService<ConnectionMonitorWorker>();
builder.Services.AddHostedService<InstallService>();

var host = builder.Build();
await host.RunAsync();
