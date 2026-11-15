using System.Text.RegularExpressions;

namespace Droidwart.Worker.Inventory;

/// <summary>Parsers for the text output of getprop, dumpsys and pm.</summary>
public static partial class AndroidParsers
{
    [GeneratedRegex(@"\bversionCode=(\d+)")]
    private static partial Regex VersionCodeRegex();

    [GeneratedRegex(@"\bminSdk=(\d+)")]
    private static partial Regex MinSdkRegex();

    [GeneratedRegex(@"\btargetSdk=(\d+)")]
    private static partial Regex TargetSdkRegex();

    [GeneratedRegex(@"^package:(\S+?)(?:\s+installer=(\S+))?\s*$")]
    private static partial Regex PmListLineRegex();

    [GeneratedRegex(@"^\d+:\s+(\S+?)\s+inet\s+(\d{1,3}(?:\.\d{1,3}){3})/\d+\b")]
    private static partial Regex Ipv4InterfaceRegex();

    [GeneratedRegex(@"^\d+:\s+([^:@]+)(?:@[^:]+)?:.*\blink/ether\s+([0-9a-fA-F:]{17})\b")]
    private static partial Regex MacInterfaceRegex();

    /// <summary>Lines in the format "key=value".</summary>
    public static Dictionary<string, string> ParseKeyValueLines(string output)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in SplitLines(output))
        {
            var separator = line.IndexOf('=');
            if (separator > 0)
            {
                result[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        return result;
    }

    /// <summary>
    /// Reads the "Package [name]" block from "dumpsys package name".
    /// Only the first block (installed version) counts, not "Hidden system packages".
    /// </summary>
    public static PackageDetails? ParseDumpsysPackage(string output, string packageName)
    {
        var lines = SplitLines(output);
        var header = $"Package [{packageName}]";
        var start = Array.FindIndex(lines, l => l.TrimStart().StartsWith(header, StringComparison.Ordinal));
        if (start < 0)
        {
            return null;
        }

        string? versionName = null, installer = null, firstInstall = null, lastUpdate = null;
        long? versionCode = null;
        int? minSdk = null, targetSdk = null;

        for (var i = start + 1; i < lines.Length; i++)
        {
            var raw = lines[i];
            var line = raw.Trim();

            // End of block: next package or a new top-level section
            if (line.StartsWith("Package [", StringComparison.Ordinal) || (raw.Length > 0 && !char.IsWhiteSpace(raw[0])))
            {
                break;
            }

            versionCode ??= MatchLong(VersionCodeRegex(), line);
            minSdk ??= MatchInt(MinSdkRegex(), line);
            targetSdk ??= MatchInt(TargetSdkRegex(), line);
            versionName ??= ValueAfter(line, "versionName=");
            installer ??= ValueAfter(line, "installerPackageName=");
            firstInstall ??= ValueAfter(line, "firstInstallTime=");
            lastUpdate ??= ValueAfter(line, "lastUpdateTime=");
        }

        if (installer == "null")
        {
            installer = null;
        }

        return new PackageDetails(packageName, versionName, versionCode, minSdk, targetSdk, installer, firstInstall, lastUpdate);
    }

    /// <summary>Output of "pm list packages -3 -i".</summary>
    public static List<InstalledPackage> ParsePmListPackages(string output)
    {
        var result = new List<InstalledPackage>();
        foreach (var line in SplitLines(output))
        {
            var match = PmListLineRegex().Match(line.Trim());
            if (!match.Success)
            {
                continue;
            }

            var installer = match.Groups[2].Success ? match.Groups[2].Value : null;
            result.Add(new InstalledPackage(match.Groups[1].Value, installer is null or "null" ? null : installer));
        }

        return result.OrderBy(p => p.PackageName, StringComparer.Ordinal).ToList();
    }

    /// <summary>Output of "cmd package resolve-activity --brief package": the last line is "package/.Activity".</summary>
    public static string? ParseResolveActivity(string output) =>
        SplitLines(output)
            .Select(l => l.Trim())
            .LastOrDefault(l => l.Contains('/') && !l.Contains('=') && !l.Contains(' '));

    /// <summary>Returns the preferred global IPv4 address and the Wi-Fi MAC address when available.</summary>
    public static (string? IpAddress, string? MacAddress) ParseNetworkInfo(string addresses, string links)
    {
        var macAddresses = SplitLines(links)
            .Select(line => MacInterfaceRegex().Match(line.Trim()))
            .Where(match => match.Success)
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value, StringComparer.Ordinal);

        var interfaces = SplitLines(addresses)
            .Select(line => Ipv4InterfaceRegex().Match(line.Trim()))
            .Where(match => match.Success)
            .Select(match => (Name: match.Groups[1].Value.Split('@')[0], Address: match.Groups[2].Value))
            .Where(item => !item.Name.StartsWith("lo", StringComparison.Ordinal))
            .ToList();

        var wifiMacAddress = macAddresses.FirstOrDefault(pair => pair.Key.StartsWith("wlan", StringComparison.Ordinal)).Value;
        var preferred = interfaces.FirstOrDefault(item => item.Name.StartsWith("wlan", StringComparison.Ordinal));
        var selected = preferred == default ? interfaces.FirstOrDefault() : preferred;
        if (selected == default)
        {
            return (null, wifiMacAddress);
        }

        return (selected.Address, wifiMacAddress ?? macAddresses.GetValueOrDefault(selected.Name));
    }

    private static string[] SplitLines(string output) =>
        output.Replace("\r", "", StringComparison.Ordinal).Split('\n');

    private static string? ValueAfter(string line, string prefix) =>
        line.StartsWith(prefix, StringComparison.Ordinal) ? line[prefix.Length..].Trim() : null;

    private static long? MatchLong(Regex regex, string line)
    {
        var match = regex.Match(line);
        return match.Success && long.TryParse(match.Groups[1].Value, out var value) ? value : null;
    }

    private static int? MatchInt(Regex regex, string line)
    {
        var match = regex.Match(line);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : null;
    }
}
