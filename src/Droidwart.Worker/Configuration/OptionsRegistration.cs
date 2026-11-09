using Microsoft.Extensions.Options;

namespace Droidwart.Worker.Configuration;

public static class OptionsRegistration
{
    public static IServiceCollection AddDroidwartOptions(this IServiceCollection services)
    {
        services.AddOptions<AdbOptions>()
            .BindConfiguration(AdbOptions.Section)
            .Validate(o => !string.IsNullOrWhiteSpace(o.ServerHost), "Adb:ServerHost is missing.")
            .Validate(o => o.ServerPort is > 0 and <= 65535, "Adb:ServerPort is invalid.")
            .Validate(o => o.CommandTimeout > TimeSpan.Zero, "Adb:CommandTimeout must be positive.")
            .ValidateOnStart();

        services.AddOptions<ScheduleOptions>()
            .BindConfiguration(ScheduleOptions.Section)
            .Validate(o => o.InventoryInterval >= TimeSpan.FromMinutes(1), "Schedule:InventoryInterval must be at least 1 minute.")
            .Validate(o => o.ConnectionCheckInterval >= TimeSpan.FromSeconds(10), "Schedule:ConnectionCheckInterval must be at least 10 seconds.")
            .ValidateOnStart();

        services.AddOptions<StorageOptions>()
            .BindConfiguration(StorageOptions.Section)
            .Validate(o => !string.IsNullOrWhiteSpace(o.DataDirectory), "Storage:DataDirectory is missing.")
            .ValidateOnStart();

        services.AddOptions<GitHubOptions>()
            .BindConfiguration(GitHubOptions.Section)
            .Validate(o => Uri.TryCreate(o.ApiBaseUrl, UriKind.Absolute, out _) && o.ApiBaseUrl.EndsWith('/'),
                "GitHub:ApiBaseUrl must be an absolute URL ending with '/'.")
            .ValidateOnStart();

        services.AddOptions<MqttOptions>()
            .BindConfiguration(MqttOptions.Section)
            .Validate(o => o.Port is > 0 and <= 65535, "Mqtt:Port is invalid.")
            .Validate(o => IsValidTopicSegment(o.BaseTopic), "Mqtt:BaseTopic must not be empty or contain '/', '+' or '#'.")
            .Validate(o => IsValidTopicSegment(o.DiscoveryPrefix), "Mqtt:DiscoveryPrefix must not be empty or contain '/', '+' or '#'.")
            .ValidateOnStart();

        // Devices and Apps live at the top level of the configuration.
        services.AddOptions<FleetOptions>()
            .Configure<IConfiguration>((options, configuration) => configuration.Bind(options))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<FleetOptions>, FleetOptionsValidator>();

        return services;
    }

    private static bool IsValidTopicSegment(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.IndexOfAny(['/', '+', '#']) < 0;
}
