using System.Text.RegularExpressions;

namespace Droidwart.Worker.Updates;

/// <summary>
/// Compares versions by their numeric core only. Tags and versionName values look different per project
/// ("v31.1", "2026.11.1-full", "0.118.3+github-debug"), but the dotted numbers in front always match.
/// Prereleases are excluded earlier (IncludePrereleases), so suffixes can be ignored safely.
/// </summary>
public static partial class VersionComparer
{
    [GeneratedRegex(@"\d+(?:\.\d+)*")]
    private static partial Regex NumericCoreRegex();

    /// <summary>The numeric core, e.g. "31.1" for "v31.1". Null if the value contains no number.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = NumericCoreRegex().Match(value);
        return match.Success ? match.Value : null;
    }

    /// <summary>Negative if <paramref name="installed"/> is older, 0 if equal, positive if newer, null if not comparable.</summary>
    public static int? Compare(string? installed, string? latest)
    {
        var left = Parse(Normalize(installed));
        var right = Parse(Normalize(latest));
        if (left is null || right is null)
        {
            return null;
        }

        var length = Math.Max(left.Length, right.Length);
        for (var i = 0; i < length; i++)
        {
            var a = i < left.Length ? left[i] : 0;
            var b = i < right.Length ? right[i] : 0;
            if (a != b)
            {
                return a < b ? -1 : 1;
            }
        }

        return 0;
    }

    private static long[]? Parse(string? core)
    {
        if (core is null)
        {
            return null;
        }

        var parts = core.Split('.');
        var numbers = new long[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!long.TryParse(parts[i], out numbers[i]))
            {
                return null;
            }
        }

        return numbers;
    }
}
