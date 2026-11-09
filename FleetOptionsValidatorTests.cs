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
            new AppOptions { Id = "termux", Repo = "termux/termux-app", Package = "com.termux", AssetPattern = "github.*{abi}.*\\.apk$" },
            new AppOptions { Id = "ip-camera", Repo = "DigitallyRefined/android-ip-camera", Package = null },
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
        fleet.Apps[0].Package = package;

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
    public void ResolvePackage_PrefersDeviceOverride()
    {
        var fleet = ValidFleet();
        var magisk = new AppOptions { Id = "magisk", Repo = "topjohnwu/Magisk", Package = "com.topjohnwu.magisk" };
        fleet.Devices[0].PackageOverrides["magisk"] = "io.example.hidden";

        Assert.Equal("io.example.hidden", FleetOptions.ResolvePackage(magisk, fleet.Devices[0]));
        Assert.Equal("com.topjohnwu.magisk", FleetOptions.ResolvePackage(magisk, fleet.Devices[1]));
    }
}
