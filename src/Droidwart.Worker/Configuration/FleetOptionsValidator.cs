using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Droidwart.Worker.Configuration;

/// <summary>
/// Validates the device and app configuration at startup.
/// Package names are validated strictly because they are inserted into shell commands on the device.
/// </summary>
public sealed partial class FleetOptionsValidator : IValidateOptions<FleetOptions>
{
    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex IdRegex();

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")]
    private static partial Regex RepoRegex();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$")]
    private static partial Regex PackageRegex();

    [GeneratedRegex(@"^[A-Za-z0-9.-]+$")]
    private static partial Regex HostRegex();

    public static bool IsValidPackageName(string? value) => value is not null && PackageRegex().IsMatch(value);

    public ValidateOptionsResult Validate(string? name, FleetOptions options)
    {
        var errors = new List<string>();

        if (options.Devices.Count == 0)
        {
            errors.Add("No devices configured (section 'Devices').");
        }

        var deviceIds = options.Devices.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        var appIds = options.Apps.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);

        errors.AddRange(Duplicates(options.Devices.Select(d => d.Id), "Device id"));
        errors.AddRange(Duplicates(options.Apps.Select(a => a.Id), "App id"));

        if (options.Devices.Count(d => d.Canary) > 1)
        {
            errors.Add("At most one device may be marked as canary.");
        }

        foreach (var device in options.Devices)
        {
            var prefix = $"Device '{device.Id}'";

            if (!IdRegex().IsMatch(device.Id))
                errors.Add($"{prefix}: id must consist of lowercase letters, digits and '-'.");
            if (string.IsNullOrWhiteSpace(device.Name))
                errors.Add($"{prefix}: name is missing.");
            if (!HostRegex().IsMatch(device.Host))
                errors.Add($"{prefix}: host '{device.Host}' is invalid.");
            if (device.Port is < 1 or > 65535)
                errors.Add($"{prefix}: port {device.Port} is invalid.");

            foreach (var (appId, package) in device.PackageOverrides)
            {
                if (!appIds.Contains(appId))
                    errors.Add($"{prefix}: PackageOverrides references unknown app '{appId}'.");
                if (!IsValidPackageName(package))
                    errors.Add($"{prefix}: package name '{package}' for '{appId}' is invalid.");
            }
        }

        foreach (var app in options.Apps)
        {
            var prefix = $"App '{app.Id}'";

            if (!IdRegex().IsMatch(app.Id))
                errors.Add($"{prefix}: id must consist of lowercase letters, digits and '-'.");
            if (!RepoRegex().IsMatch(app.Repo))
                errors.Add($"{prefix}: repo '{app.Repo}' must use the format 'owner/name'.");
            if (!string.IsNullOrEmpty(app.Package) && !IsValidPackageName(app.Package))
                errors.Add($"{prefix}: package name '{app.Package}' is invalid.");

            foreach (var alternative in app.AlternativePackages.Where(p => !IsValidPackageName(p)))
                errors.Add($"{prefix}: alternative package name '{alternative}' is invalid.");

            foreach (var deviceId in app.Devices.Where(id => !deviceIds.Contains(id)))
                errors.Add($"{prefix}: references unknown device '{deviceId}'.");

            try
            {
                _ = new Regex(app.AssetPattern.Replace("{abi}", "arm64-v8a", StringComparison.Ordinal));
            }
            catch (ArgumentException ex)
            {
                errors.Add($"{prefix}: AssetPattern is not a valid regular expression ({ex.Message}).");
            }
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static IEnumerable<string> Duplicates(IEnumerable<string> ids, string label) =>
        ids.GroupBy(id => id, StringComparer.Ordinal)
           .Where(g => g.Count() > 1)
           .Select(g => $"{label} '{g.Key}' is used more than once.");
}
