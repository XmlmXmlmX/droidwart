using System.Security.Cryptography;
using Droidwart.Worker.Configuration;
using Droidwart.Worker.Updates;
using Microsoft.Extensions.Options;

namespace Droidwart.Worker.Installs;

/// <summary>
/// Downloads release assets into the data directory (data/apks/&lt;app&gt;/) and verifies the SHA-256
/// checksum GitHub publishes for each asset. Only https://github.com release asset URLs are accepted.
/// </summary>
public sealed class ApkDownloader(IHttpClientFactory httpClientFactory, IOptions<StorageOptions> storage, ILogger<ApkDownloader> logger)
{
    public const string HttpClientName = "downloads";

    private const int KeepPerApp = 3;

    public async Task<string> DownloadAsync(string appId, UpdateCheck update, Action<double> progress, CancellationToken cancellationToken)
    {
        if (update.AssetUrl is null || update.AssetName is null)
        {
            throw new InstallException("The release has no matching asset.");
        }

        if (!Uri.TryCreate(update.AssetUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InstallException($"Refusing to download from '{update.AssetUrl}': only https://github.com release assets are allowed.");
        }

        var fileName = Path.GetFileName(update.AssetName);
        if (fileName.Length == 0 || fileName != update.AssetName || !fileName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
        {
            throw new InstallException($"Unexpected asset name '{update.AssetName}'.");
        }

        var directory = Path.Combine(storage.Value.DataDirectory, "apks", appId);
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, fileName);
        var expected = ParseSha256(update.AssetDigest);

        if (File.Exists(target) && expected is not null && await HashFileAsync(target, cancellationToken) == expected)
        {
            logger.LogInformation("Using cached {Asset}", fileName);
            progress(100);
            return target;
        }

        var temp = target + ".part";
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InstallException($"Download of {fileName} failed with HTTP {(int)response.StatusCode}.");
        }

        var total = response.Content.Headers.ContentLength ?? update.AssetSize ?? 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var file = File.Create(temp))
        {
            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                hash.AppendData(buffer, 0, read);
                received += read;
                if (total > 0)
                {
                    progress(Math.Min(100, received * 100d / total));
                }
            }
        }

        var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (expected is null)
        {
            logger.LogWarning("Release asset {Asset} has no checksum on GitHub; verification skipped", fileName);
        }
        else if (actual != expected)
        {
            File.Delete(temp);
            throw new InstallException($"Checksum mismatch for {fileName} (expected {expected[..12]}…, got {actual[..12]}…).");
        }

        File.Move(temp, target, overwrite: true);
        CleanUp(directory);
        logger.LogInformation("Downloaded {Asset} ({Size:N0} bytes, sha256 {Hash}…)", fileName, new FileInfo(target).Length, actual[..12]);
        return target;
    }

    /// <summary>"sha256:ABC…" → "abc…"; null for missing or other algorithms.</summary>
    public static string? ParseSha256(string? digest) =>
        digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && digest.Length > 7
            ? digest[7..].Trim().ToLowerInvariant()
            : null;

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    /// <summary>Keeps the newest APKs per app (older versions are kept for a later rollback).</summary>
    private void CleanUp(string directory)
    {
        foreach (var old in new DirectoryInfo(directory).GetFiles("*.apk")
                     .OrderByDescending(f => f.LastWriteTimeUtc)
                     .Skip(KeepPerApp))
        {
            try
            {
                old.Delete();
            }
            catch (IOException ex)
            {
                logger.LogDebug(ex, "Could not delete {File}", old.FullName);
            }
        }
    }
}
