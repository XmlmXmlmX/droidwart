namespace Droidwart.Worker.Configuration;

public sealed class GitHubOptions
{
    public const string Section = "GitHub";

    /// <summary>Base URL of the GitHub REST API (must end with "/").</summary>
    public string ApiBaseUrl { get; set; } = "https://api.github.com/";

    /// <summary>Optional token (read-only access to public repositories is enough). Raises the rate limit.</summary>
    public string? Token { get; set; }
}
