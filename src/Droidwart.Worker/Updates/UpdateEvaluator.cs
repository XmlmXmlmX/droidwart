using Droidwart.Worker.Configuration;
using Droidwart.Worker.GitHub;
using Droidwart.Worker.Inventory;

namespace Droidwart.Worker.Updates;

public enum UpdateState
{
    UpToDate,
    UpdateAvailable,
    NotInstalled,
    InstalledNewer,
    Unknown,
    CheckFailed,
}

/// <summary>Comparison of the installed app with the latest GitHub release, including the asset to install.</summary>
public sealed record UpdateCheck(
    UpdateState State,
    string? LatestTag,
    string? LatestVersion,
    string? ReleaseUrl,
    DateTimeOffset? PublishedAt,
    string? AssetName,
    string? AssetUrl,
    long? AssetSize,
    string? AssetDigest,
    string? ReleaseNotes,
    string? Problem)
{
    /// <summary>True if Droidwart could install or update the app (an asset was found).</summary>
    public bool Installable => AssetUrl is not null && State is UpdateState.UpdateAvailable or UpdateState.NotInstalled;

    public static UpdateCheck Failed(string problem) =>
        new(UpdateState.CheckFailed, null, null, null, null, null, null, null, null, null, problem);
}

public static class UpdateEvaluator
{
    public static UpdateCheck Evaluate(
        AppVariant variant, IReadOnlyList<string> deviceAbis, PackageDetails? installed, ReleaseLookup lookup)
    {
        if (lookup.Release is not { } release)
        {
            return UpdateCheck.Failed(lookup.Error ?? "No release information.");
        }

        var asset = AssetSelector.Select(release.Assets, variant.AssetPattern, deviceAbis, out var problem);

        UpdateState state;
        if (installed is null)
        {
            state = UpdateState.NotInstalled;
        }
        else
        {
            state = VersionComparer.Compare(installed.VersionName, release.TagName) switch
            {
                null => UpdateState.Unknown,
                < 0 => UpdateState.UpdateAvailable,
                0 => UpdateState.UpToDate,
                > 0 => UpdateState.InstalledNewer,
            };

            if (state == UpdateState.Unknown)
            {
                problem ??= $"Cannot compare installed version '{installed.VersionName}' with tag '{release.TagName}'.";
            }
        }

        return new UpdateCheck(
            state,
            release.TagName,
            VersionComparer.Normalize(release.TagName),
            release.HtmlUrl,
            release.PublishedAt,
            asset?.Name,
            asset?.BrowserDownloadUrl,
            asset?.Size,
            asset?.Digest,
            release.Body,
            problem);
    }
}
