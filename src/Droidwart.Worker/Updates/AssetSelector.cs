using System.Text.RegularExpressions;
using Droidwart.Worker.GitHub;

namespace Droidwart.Worker.Updates;

public static class AssetSelector
{
    public const string AbiPlaceholder = "{abi}";

    /// <summary>
    /// Picks exactly one asset. With {abi} in the pattern, the device ABIs are tried in order
    /// (primary ABI first), so a 64-bit device gets the arm64-v8a build before armeabi-v7a.
    /// </summary>
    public static GitHubAsset? Select(
        IReadOnlyList<GitHubAsset> assets, string pattern, IReadOnlyList<string> deviceAbis, out string? problem)
    {
        var usesAbi = pattern.Contains(AbiPlaceholder, StringComparison.Ordinal);
        IReadOnlyList<string?> candidates = usesAbi ? deviceAbis.Cast<string?>().ToList() : new string?[] { null };

        foreach (var abi in candidates)
        {
            var effective = abi is null ? pattern : pattern.Replace(AbiPlaceholder, Regex.Escape(abi), StringComparison.Ordinal);
            var regex = new Regex(effective, RegexOptions.CultureInvariant);
            var matches = assets.Where(a => regex.IsMatch(a.Name)).ToList();

            if (matches.Count == 1)
            {
                problem = null;
                return matches[0];
            }

            if (matches.Count > 1)
            {
                problem = $"Asset pattern matches several files: {string.Join(", ", matches.Select(m => m.Name))}.";
                return null;
            }
        }

        problem = usesAbi
            ? $"No asset matches the pattern for the device ABIs ({string.Join(", ", deviceAbis)})."
            : "No asset matches the pattern.";
        return null;
    }
}
