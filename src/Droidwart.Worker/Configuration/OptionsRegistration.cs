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
            .ValidateOnStart();

        services.AddOptions<StorageOptions>()
            .BindConfiguration(StorageOptions.Section)
            .Validate(o => !string.IsNullOrWhiteSpace(o.DataDirectory), "Storage:DataDirectory is missing.")
            .ValidateOnStart();

        // Devices and Apps live at the top level of the configuration.
        services.AddOptions<FleetOptions>()
            .Configure<IConfiguration>((options, configuration) => configuration.Bind(options))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<FleetOptions>, FleetOptionsValidator>();

        return services;
    }
}
