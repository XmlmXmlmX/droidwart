using Droidwart.Worker.Adb;
using System.Net.Http.Headers;
using Droidwart.Worker.Configuration;
using Droidwart.Worker.GitHub;
using Droidwart.Worker.Inventory;
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
});
builder.Services.AddSingleton<GitHubReleaseClient>();
builder.Services.AddSingleton<InventoryCollector>();
builder.Services.AddSingleton<InventoryReporter>();
builder.Services.AddHostedService<InventoryWorker>();

var host = builder.Build();
await host.RunAsync();
