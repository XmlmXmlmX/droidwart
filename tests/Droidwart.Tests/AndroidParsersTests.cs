using Droidwart.Worker.Adb;
using Droidwart.Worker.Inventory;
using Xunit;

namespace Droidwart.Tests;

public class AndroidParsersTests
{
    private const string TermuxDump = """
        Activity Resolver Table:
          Non-Data Actions:
              android.intent.action.MAIN:
                7d1f0a1 com.termux/.app.TermuxActivity filter 5c0e5e2

        Packages:
          Package [com.termux] (8e3b6a1):
            userId=10123
            pkg=Package{1b2c3d4 com.termux}
            versionCode=1002 minSdk=24 targetSdk=28
            versionName=0.118.0
            splits=[base]
            firstInstallTime=2024-03-01 18:00:12
            lastUpdateTime=2025-01-12 10:22:34
            installerPackageName=org.fdroid.fdroid

        Hidden system packages:
          Package [com.termux] (aaaaaaa):
            versionCode=1 minSdk=24 targetSdk=28
            versionName=0.1
        """;

    [Fact]
    public void ParseDumpsysPackage_ReadsFirstBlockOnly()
    {
        var details = AndroidParsers.ParseDumpsysPackage(TermuxDump, "com.termux");

        Assert.NotNull(details);
        Assert.Equal("0.118.0", details.VersionName);
        Assert.Equal(1002, details.VersionCode);
        Assert.Equal(24, details.MinSdk);
        Assert.Equal(28, details.TargetSdk);
        Assert.Equal("org.fdroid.fdroid", details.Installer);
        Assert.Equal("2025-01-12 10:22:34", details.LastUpdateTime);
    }

    [Fact]
    public void ParseDumpsysPackage_ReturnsNull_WhenPackageMissing()
    {
        Assert.Null(AndroidParsers.ParseDumpsysPackage("Unable to find package: com.example.missing", "com.example.missing"));
        Assert.Null(AndroidParsers.ParseDumpsysPackage(TermuxDump, "com.termux.boot"));
    }

    [Fact]
    public void ParseDumpsysPackage_TreatsNullInstallerAsUnknown()
    {
        const string dump = """
            Packages:
              Package [io.homeassistant.companion.android] (1):
                versionCode=12345 minSdk=21 targetSdk=35
                versionName=2026.11.1-full
                installerPackageName=null
            """;

        var details = AndroidParsers.ParseDumpsysPackage(dump, "io.homeassistant.companion.android");

        Assert.NotNull(details);
        Assert.Equal("2026.11.1-full", details.VersionName);
        Assert.Null(details.Installer);
    }

    [Fact]
    public void ParsePmListPackages_HandlesInstallerVariants()
    {
        const string output = "package:com.termux  installer=org.fdroid.fdroid\r\n" +
                              "package:mattecarra.accapp installer=null\n" +
                              "package:com.topjohnwu.magisk\n" +
                              "garbage line\n";

        var packages = AndroidParsers.ParsePmListPackages(output);

        Assert.Equal(3, packages.Count);
        Assert.Contains(new InstalledPackage("com.termux", "org.fdroid.fdroid"), packages);
        Assert.Contains(new InstalledPackage("mattecarra.accapp", null), packages);
        Assert.Contains(new InstalledPackage("com.topjohnwu.magisk", null), packages);
    }

    [Fact]
    public void ParseKeyValueLines_ReadsGetpropLoop()
    {
        const string output = "ro.product.model=SM-G930F\nro.product.cpu.abilist=arm64-v8a,armeabi-v7a,armeabi\nro.build.version.sdk=26\n";

        var props = AndroidParsers.ParseKeyValueLines(output);

        Assert.Equal("SM-G930F", props["ro.product.model"]);
        Assert.Equal("arm64-v8a,armeabi-v7a,armeabi", props["ro.product.cpu.abilist"]);
        Assert.Equal("26", props["ro.build.version.sdk"]);
    }

    [Fact]
    public void ParseResolveActivity_ReturnsComponent()
    {
        const string output = "priority=0 preferredOrder=0 match=0x108000 specificIndex=-1 isDefault=true\ncom.termux/.app.TermuxActivity\n";

        Assert.Equal("com.termux/.app.TermuxActivity", AndroidParsers.ParseResolveActivity(output));
        Assert.Null(AndroidParsers.ParseResolveActivity("No activity found"));
    }

    [Theory]
    [InlineData("hello\n__DROIDWART_EXIT__0\n", "hello", 0)]
    [InlineData("Error: oops\n__DROIDWART_EXIT__1", "Error: oops", 1)]
    [InlineData("no marker", "no marker", null)]
    public void ParseShellOutput_ExtractsExitCode(string raw, string expectedOutput, int? expectedCode)
    {
        var result = AdbGateway.ParseShellOutput(raw);

        Assert.Equal(expectedOutput, result.Output);
        Assert.Equal(expectedCode, result.ExitCode);
    }
}
