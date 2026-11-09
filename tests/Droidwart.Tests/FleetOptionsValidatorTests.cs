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
                Variants = { new AppVariant { Package = "com.termux", AssetPattern = @"^termux-app_v[\d.]+\+github-debug_{abi}\.apk$" } },
            },
            new AppOptions
            {
                Id = "magisk",
                Repo = "topjohnwu/Magisk",
                Variants = { new AppVariant { Package = "com.topjohnwu.magisk", AssetPattern = @"^Magisk-v[\d.]+\.apk$" } },
            },
        },
    };

    private static readonly FleetOptionsValidator Validator = new();

    [Fact]
    public void ValidConfiguration_Succeeds()
    {
        var result = Validator.Validate(null, ValidFleet());

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Failures ?? []));
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
    public void AppWithoutVariants_Fails()
    {
        var fleet = ValidFleet();
        fleet.Apps[0].Variants.Clear();

        Assert.True(Validator.Validate(null, fleet).Failed);
    }

    [Fact]
    public void InvalidAssetPattern_Fails()
    {
        var fleet = ValidFleet();
        fleet.Apps[0].Variants[0].AssetPattern = "([unclosed";

        Assert.True(Validator.Validate(null, fleet).Failed);
    }

    [Fact]
    public void UnknownDeviceReference_Fails()
    {
        var fleet = ValidFleet();
        fleet.Apps[0].Devices.Add("android-ip-cam-cellar");

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
    public void ResolveVariants_AppliesDeviceOverrideToDefaultVariant()
    {
        var fleet = ValidFleet();
        var magisk = fleet.Apps[1];
        fleet.Devices[0].PackageOverrides["magisk"] = "io.example.hidden";

        var overridden = FleetOptions.ResolveVariants(magisk, fleet.Devices[0]);
        var regular = FleetOptions.ResolveVariants(magisk, fleet.Devices[1]);

        Assert.Equal("io.example.hidden", overridden[0].Package);
        Assert.Equal(magisk.Variants[0].AssetPattern, overridden[0].AssetPattern);
        Assert.Equal("com.topjohnwu.magisk", regular[0].Package);
    }
}
