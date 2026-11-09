using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Droidwart.Worker.Configuration;

/// <summary>
/// Prüft die Geräte- und App-Konfiguration beim Start.
/// Paketnamen werden streng validiert, weil sie in Shell-Befehle auf dem Gerät einfließen.
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
            errors.Add("Keine Geräte konfiguriert (Abschnitt 'Devices').");
        }

        var deviceIds = options.Devices.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        var appIds = options.Apps.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);

        errors.AddRange(Duplicates(options.Devices.Select(d => d.Id), "Geräte-Id"));
        errors.AddRange(Duplicates(options.Apps.Select(a => a.Id), "App-Id"));

        if (options.Devices.Count(d => d.Canary) > 1)
        {
            errors.Add("Höchstens ein Gerät darf als Canary markiert sein.");
        }

        foreach (var device in options.Devices)
        {
            var prefix = $"Gerät '{device.Id}'";

            if (!IdRegex().IsMatch(device.Id))
                errors.Add($"{prefix}: Id muss aus Kleinbuchstaben, Ziffern und '-' bestehen.");
            if (string.IsNullOrWhiteSpace(device.Name))
                errors.Add($"{prefix}: Name fehlt.");
            if (!HostRegex().IsMatch(device.Host))
                errors.Add($"{prefix}: Host '{device.Host}' ist ungültig.");
            if (device.Port is < 1 or > 65535)
                errors.Add($"{prefix}: Port {device.Port} ist ungültig.");

            foreach (var (appId, package) in device.PackageOverrides)
            {
                if (!appIds.Contains(appId))
                    errors.Add($"{prefix}: PackageOverrides verweist auf unbekannte App '{appId}'.");
                if (!IsValidPackageName(package))
                    errors.Add($"{prefix}: Paketname '{package}' für '{appId}' ist ungültig.");
            }
        }

        foreach (var app in options.Apps)
        {
            var prefix = $"App '{app.Id}'";

            if (!IdRegex().IsMatch(app.Id))
                errors.Add($"{prefix}: Id muss aus Kleinbuchstaben, Ziffern und '-' bestehen.");
            if (!RepoRegex().IsMatch(app.Repo))
                errors.Add($"{prefix}: Repo '{app.Repo}' muss das Format 'owner/name' haben.");
            if (!string.IsNullOrEmpty(app.Package) && !IsValidPackageName(app.Package))
                errors.Add($"{prefix}: Paketname '{app.Package}' ist ungültig.");

            foreach (var alternative in app.AlternativePackages.Where(p => !IsValidPackageName(p)))
                errors.Add($"{prefix}: alternativer Paketname '{alternative}' ist ungültig.");

            foreach (var deviceId in app.Devices.Where(id => !deviceIds.Contains(id)))
                errors.Add($"{prefix}: verweist auf unbekanntes Gerät '{deviceId}'.");

            try
            {
                _ = new Regex(app.AssetPattern.Replace("{abi}", "arm64-v8a", StringComparison.Ordinal));
            }
            catch (ArgumentException ex)
            {
                errors.Add($"{prefix}: AssetPattern ist kein gültiger regulärer Ausdruck ({ex.Message}).");
            }
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static IEnumerable<string> Duplicates(IEnumerable<string> ids, string label) =>
        ids.GroupBy(id => id, StringComparer.Ordinal)
           .Where(g => g.Count() > 1)
           .Select(g => $"{label} '{g.Key}' ist mehrfach vergeben.");
}
