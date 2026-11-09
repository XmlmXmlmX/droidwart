namespace Droidwart.Worker.GitHub;

/// <summary>Subset of the GitHub release API response that Droidwart needs.</summary>
public sealed record GitHubRelease
{
    public string TagName { get; init; } = "";

    public string? Name { get; init; }

    public string HtmlUrl { get; init; } = "";

    public string? Body { get; init; }

    public bool Draft { get; init; }

    public bool Prerelease { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public List<GitHubAsset> Assets { get; init; } = [];
}

public sealed record GitHubAsset
{
    public string Name { get; init; } = "";

    public string BrowserDownloadUrl { get; init; } = "";

    public long Size { get; init; }

    /// <summary>Checksum provided by GitHub, e.g. "sha256:…". May be missing on older releases.</summary>
    public string? Digest { get; init; }
}

/// <summary>Result of looking up the latest release of a repository.</summary>
public sealed record ReleaseLookup(GitHubRelease? Release, string? Error)
{
    public static ReleaseLookup Found(GitHubRelease release) => new(release, null);

    public static ReleaseLookup Failed(string error) => new(null, error);
}
