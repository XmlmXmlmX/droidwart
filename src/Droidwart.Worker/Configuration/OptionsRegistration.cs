using Microsoft.Extensions.Options;

namespace Droidwart.Worker.Configuration;

public static class OptionsRegistration
{
    public static IServiceCollection AddDroidwartOptions(this IServiceCollection services)
    {
        services.AddOptions<AdbOptions>()
            .BindConfiguration(AdbOptions.Section)
            .Validate(o => !string.IsNullOrWhiteSpace(o.ServerHost), "Adb:ServerHost fehlt.")
            .Validate(o => o.ServerPort is > 0 and <= 65535, "Adb:ServerPort ist ungültig.")
            .Validate(o => o.CommandTimeout > TimeSpan.Zero, "Adb:CommandTimeout muss positiv sein.")
            .ValidateOnStart();

        services.AddOptions<ScheduleOptions>()
            .BindConfiguration(ScheduleOptions.Section)
            .Validate(o => o.InventoryInterval >= TimeSpan.FromMinutes(1), "Schedule:InventoryInterval muss mindestens 1 Minute betragen.")
            .ValidateOnStart();

        services.AddOptions<StorageOptions>()
            .BindConfiguration(StorageOptions.Section)
            .Validate(o => !string.IsNullOrWhiteSpace(o.DataDirectory), "Storage:DataDirectory fehlt.")
            .ValidateOnStart();

        // Devices und Apps liegen auf oberster Ebene der Konfiguration.
        services.AddOptions<FleetOptions>()
            .Configure<IConfiguration>((options, configuration) => configuration.Bind(options))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<FleetOptions>, FleetOptionsValidator>();

        return services;
    }
}
