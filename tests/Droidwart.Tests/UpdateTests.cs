using Droidwart.Worker.Configuration;
using Droidwart.Worker.GitHub;
using Droidwart.Worker.Inventory;
using Droidwart.Worker.Updates;
using Xunit;

namespace Droidwart.Tests;

public class VersionComparerTests
{
    [Theory]
    [InlineData("31.1", "v31.1", 0)]
    [InlineData("1.0.35", "v1.0.35", 0)]
    [InlineData("2026.11.1-full", "2026.11.1", 0)]
    [InlineData("0.118.3+github-debug", "v0.118.3", 0)]
    [InlineData("0.16.0", "v0.16.1", -1)]
    [InlineData("1.7", "v1.7.0", 0)]
    [InlineData("2026.10.4-minimal", "2026.11.1", -1)]
    [InlineData("31.2", "v31.1", 1)]
    [InlineData("1.10.0", "v1.9.9", 1)]
    public void Compare_UsesNumericCore(string installed, string tag, int expected)
    {
        Assert.Equal(expected, VersionComparer.Compare(installed, tag));
    }

    [Theory]
    [InlineData(null, "v1.0")]
    [InlineData("beta", "v1.0")]
    [InlineData("1.0", "latest")]
    public void Compare_ReturnsNull_WhenNotComparable(string? installed, string tag)
    {
        Assert.Null(VersionComparer.Compare(installed, tag));
    }
}

public class AssetSelectorTests
{
    private static readonly string[] Arm64Device = ["arm64-v8a", "armeabi-v7a", "armeabi"];
    private static readonly string[] Arm32Device = ["armeabi-v7a", "armeabi"];

    // Asset names of the real releases (November 2026)
    private static readonly List<GitHubAsset> Magisk = Assets("app-debug.apk", "Magisk-v31.1.apk");

    private static readonly List<GitHubAsset> HaCompanion = Assets(
        "app-full-release.apk", "app-minimal-release.apk", "automotive-full-release.apk",
        "automotive-minimal-release.apk", "locales_config.xml", "strings.zip", "version_code.txt", "wear-release.apk");

    private static readonly List<GitHubAsset> Obtainium = Assets(
        "app-arm64-v8a-fdroid-release.apk", "app-arm64-v8a-fdroid-release.apk.idsig", "app-arm64-v8a-fdroid-release.apk.sha256",
        "app-arm64-v8a-release.apk", "app-arm64-v8a-release.apk.idsig", "app-arm64-v8a-release.apk.sha256",
        "app-armeabi-v7a-fdroid-release.apk", "app-armeabi-v7a-release.apk", "app-armeabi-v7a-release.apk.sha256",
        "app-fdroid-release.apk", "app-fdroid-release.apk.idsig", "app-release.apk", "app-x86_64-release.apk");

    private static readonly List<GitHubAsset> Termux = Assets(
        "termux-app_v0.118.3+github-debug_arm64-v8a.apk", "termux-app_v0.118.3+github-debug_armeabi-v7a.apk",
        "termux-app_v0.118.3+github-debug_sha256sums", "termux-app_v0.118.3+github-debug_universal.apk",
        "termux-app_v0.118.3+github-debug_x86.apk", "termux-app_v0.118.3+github-debug_x86_64.apk");

    private static readonly List<GitHubAsset> IpCamera = Assets(
        "androidipcamera-0.16.1-arm64-v8a-release.apk", "androidipcamera-0.16.1-armeabi-v7a-release.apk",
        "androidipcamera-0.16.1-universal-release.apk");

    [Theory]
    [MemberData(nameof(Cases))]
    public void Select_PicksExactlyTheExpectedAsset(string name, List<GitHubAsset> assets, string pattern, string[] abis, string expected)
    {
        var asset = AssetSelector.Select(assets, pattern, abis, out var problem);

        Assert.True(asset is not null, $"{name}: {problem}");
        Assert.Equal(expected, asset.Name);
    }

    public static TheoryData<string, List<GitHubAsset>, string, string[], string> Cases() => new()
    {
        { "magisk", Magisk, @"^Magisk-v[\d.]+\.apk$", Arm64Device, "Magisk-v31.1.apk" },
        { "ha-full", HaCompanion, @"^app-full-release\.apk$", Arm64Device, "app-full-release.apk" },
        { "ha-minimal", HaCompanion, @"^app-minimal-release\.apk$", Arm64Device, "app-minimal-release.apk" },
        { "obtainium-64", Obtainium, @"^app-{abi}-release\.apk$", Arm64Device, "app-arm64-v8a-release.apk" },
        { "obtainium-32", Obtainium, @"^app-{abi}-release\.apk$", Arm32Device, "app-armeabi-v7a-release.apk" },
        { "obtainium-fdroid", Obtainium, @"^app-{abi}-fdroid-release\.apk$", Arm64Device, "app-arm64-v8a-fdroid-release.apk" },
        { "termux-64", Termux, @"^termux-app_v[\d.]+\+github-debug_{abi}\.apk$", Arm64Device, "termux-app_v0.118.3+github-debug_arm64-v8a.apk" },
        { "termux-32", Termux, @"^termux-app_v[\d.]+\+github-debug_{abi}\.apk$", Arm32Device, "termux-app_v0.118.3+github-debug_armeabi-v7a.apk" },
        { "ip-camera", IpCamera, @"^androidipcamera-[\d.]+-{abi}-release\.apk$", Arm64Device, "androidipcamera-0.16.1-arm64-v8a-release.apk" },
    };

    [Fact]
    public void Select_ReportsAmbiguousPattern()
    {
        var asset = AssetSelector.Select(HaCompanion, @"full.*\.apk$", Arm64Device, out var problem);

        Assert.Null(asset);
        Assert.Contains("automotive-full-release.apk", problem);
    }

    [Fact]
    public void Select_ReportsMissingAsset()
    {
        var asset = AssetSelector.Select(IpCamera, @"^androidipcamera-[\d.]+-{abi}-release\.apk$", ["x86"], out var problem);

        Assert.Null(asset);
        Assert.Contains("x86", problem);
    }

    private static List<GitHubAsset> Assets(params string[] names) =>
        names.Select(n => new GitHubAsset { Name = n, BrowserDownloadUrl = $"https://example.invalid/{n}", Size = 1 }).ToList();
}

public class UpdateEvaluatorTests
{
    private static readonly AppVariant MagiskVariant = new() { Package = "com.topjohnwu.magisk", AssetPattern = @"^Magisk-v[\d.]+\.apk$" };

    private static readonly ReleaseLookup MagiskRelease = ReleaseLookup.Found(new GitHubRelease
    {
        TagName = "v31.1",
        HtmlUrl = "https://github.com/topjohnwu/Magisk/releases/tag/v31.1",
        Assets = [new GitHubAsset { Name = "Magisk-v31.1.apk", BrowserDownloadUrl = "https://example.invalid/Magisk-v31.1.apk", Size = 11 }],
    });

    private static PackageDetails Installed(string version) =>
        new("com.topjohnwu.magisk", version, 1, 24, 37, null, null, null);

    [Fact]
    public void SameVersion_IsUpToDate()
    {
        var check = UpdateEvaluator.Evaluate(MagiskVariant, ["arm64-v8a"], Installed("31.1"), MagiskRelease);

        Assert.Equal(UpdateState.UpToDate, check.State);
        Assert.False(check.Installable);
    }

    [Fact]
    public void OlderVersion_HasUpdateWithAsset()
    {
        var check = UpdateEvaluator.Evaluate(MagiskVariant, ["arm64-v8a"], Installed("30.6"), MagiskRelease);

        Assert.Equal(UpdateState.UpdateAvailable, check.State);
        Assert.Equal("Magisk-v31.1.apk", check.AssetName);
        Assert.Equal("31.1", check.LatestVersion);
        Assert.True(check.Installable);
    }

    [Fact]
    public void MissingApp_IsInstallable()
    {
        var check = UpdateEvaluator.Evaluate(MagiskVariant, ["arm64-v8a"], null, MagiskRelease);

        Assert.Equal(UpdateState.NotInstalled, check.State);
        Assert.True(check.Installable);
    }

    [Fact]
    public void FailedLookup_IsReported()
    {
        var check = UpdateEvaluator.Evaluate(MagiskVariant, ["arm64-v8a"], Installed("31.1"), ReleaseLookup.Failed("rate limit"));

        Assert.Equal(UpdateState.CheckFailed, check.State);
        Assert.Equal("rate limit", check.Problem);
    }
}
