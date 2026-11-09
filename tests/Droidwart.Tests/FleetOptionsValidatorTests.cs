using Droidwart.Worker.Configuration;
using Xunit;

namespace Droidwart.Tests;

public class FleetOptionsValidatorTests
{
    private static FleetOptions ValidFleet() => new()
    {
        Devices =
        {
            new DeviceOptions { Id = "android-ip-cam-garden", Name = "Garden", Host = "192.168.178.164", Canary = true },
            new DeviceOptions { Id = "android-ip-cam-garage", Name = "Garage", Host = "192.168.178.160" },
        },
        Apps =
        {
            new AppOptions
            {
                Id = "termux",
                Repo = "termux/termux-app",
                Variants = { new AppVariant { Package = "com.termux", AssetPattern = "github.*{abi}.*\\.apk$" } },
            },
            new AppOptions
            {
                Id = "ip-camera",
                Repo = "DigitallyRefined/android-ip-camera",
                Variants = { new AppVariant { Package = "com.example.ipcamera" } },
            },
        },
    };

    private static readonly FleetOptionsValidator Validator = new();

    [Fact]
    public void ValidConfiguration_Succeeds()
    {
        Assert.True(Validator.Validate(null, ValidFleet()).Succeeded);
    }

    [Fact]
    public void DuplicateDeviceId_Fails()
    {
        var fleet = ValidFleet();
        fleet.Devices[1].Id = fleet.Devices[0].Id;

        var result = Validator.Validate(null, fleet);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("more than once", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("com.termux; reboot")]
    [InlineData("com.termux$(id)")]
    [InlineData("termux")]
    public void UnsafeOrInvalidPackageName_Fails(string package)
    {
        var fleet = ValidFleet();
        fleet.Apps[0].Variants[0].Package = package;

        Assert.True(Validator.Validate(null, fleet).Failed);
    }

    [Fact]
    public void UnknownDeviceReference_Fails()
    {
        var fleet = ValidFleet();
        fleet.Apps[0].Devices.Add("android-ip-cam-keller");

        Assert.True(Validator.Validate(null, fleet).Failed);
    }

    [Fact]
    public void InvalidRepo_Fails()
    {
        var fleet = ValidFleet();
        fleet.Apps[0].Repo = "https://github.com/termux/termux-app";

        Assert.True(Validator.Validate(null, fleet).Failed);
    }

    [Fact]
    public void ResolveVariants_PrefersDevicePackageOverride()
    {
        var fleet = ValidFleet();
        var magisk = new AppOptions
        {
            Id = "magisk",
            Repo = "topjohnwu/Magisk",
            Variants =
            {
                new AppVariant { Package = "com.topjohnwu.magisk", AssetPattern = "^Magisk-v.*\\.apk$" },
            },
        };
        fleet.Devices[0].PackageOverrides["magisk"] = "io.example.hidden";

        var overridden = FleetOptions.ResolveVariants(magisk, fleet.Devices[0]);
        var unchanged = FleetOptions.ResolveVariants(magisk, fleet.Devices[1]);

        Assert.Equal("io.example.hidden", overridden[0].Package);
        Assert.Equal("^Magisk-v.*\\.apk$", overridden[0].AssetPattern);
        Assert.Equal("com.topjohnwu.magisk", unchanged[0].Package);
    }
}
