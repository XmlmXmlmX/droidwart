using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Droidwart.Worker.GitHub;

/// <summary>
/// Reads the latest release of a repository. Responses are cached with their ETag, so unchanged
/// releases are answered with "304 Not Modified" and barely touch the API rate limit.
/// </summary>
public sealed class GitHubReleaseClient(IHttpClientFactory httpClientFactory, ILogger<GitHubReleaseClient> logger)
{
    public const string HttpClientName = "github";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    public async Task<ReleaseLookup> GetLatestAsync(string repo, bool includePrereleases, CancellationToken cancellationToken)
    {
        // "releases/latest" ignores drafts and prereleases; with prereleases we take the newest non-draft.
        var path = includePrereleases ? $"repos/{repo}/releases?per_page=10" : $"repos/{repo}/releases/latest";

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        _cache.TryGetValue(path, out var cached);
        if (cached?.ETag is { } etag && EntityTagHeaderValue.TryParse(etag, out var parsedTag))
        {
            request.Headers.IfNoneMatch.Add(parsedTag);
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
            {
                return ReleaseLookup.Found(cached.Release);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return ReleaseLookup.Failed($"No release found in {repo}.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return ReleaseLookup.Failed($"GitHub returned {(int)response.StatusCode} for {repo}: {Shorten(body)}");
            }

            GitHubRelease? release;
            if (includePrereleases)
            {
                var releases = await response.Content.ReadFromJsonAsync<List<GitHubRelease>>(JsonOptions, cancellationToken);
                release = releases?.FirstOrDefault(r => !r.Draft);
            }
            else
            {
                release = await response.Content.ReadFromJsonAsync<GitHubRelease>(JsonOptions, cancellationToken);
            }

            if (release is null)
            {
                return ReleaseLookup.Failed($"No release found in {repo}.");
            }

            _cache[path] = new CacheEntry(response.Headers.ETag?.ToString(), release);
            LogRateLimit(response, repo);
            return ReleaseLookup.Found(release);
        }
        catch (Exception ex) when ((ex is HttpRequestException or JsonException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not read the latest release of {Repo}", repo);
            return ReleaseLookup.Failed($"Could not read the latest release of {repo}: {ex.Message}");
        }
    }

    private void LogRateLimit(HttpResponseMessage response, string repo)
    {
        if (response.Headers.TryGetValues("x-ratelimit-remaining", out var values)
            && int.TryParse(values.FirstOrDefault(), out var remaining)
            && remaining < 10)
        {
            logger.LogWarning("GitHub rate limit almost used up ({Remaining} requests left, last: {Repo}). Consider setting GitHub:Token.",
                remaining, repo);
        }
    }

    private static string Shorten(string text) => text.Length <= 200 ? text : text[..200] + "…";

    private sealed record CacheEntry(string? ETag, GitHubRelease Release);
}
