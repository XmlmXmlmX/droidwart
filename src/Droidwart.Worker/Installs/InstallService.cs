using System.Diagnostics;
using System.Text.RegularExpressions;
using Droidwart.Worker.Adb;
using Droidwart.Worker.Configuration;
using Droidwart.Worker.Inventory;
using Droidwart.Worker.Mqtt;
using Droidwart.Worker.Services;
using Droidwart.Worker.Updates;
using Microsoft.Extensions.Options;

namespace Droidwart.Worker.Installs;

/// <summary>
/// Processes install and app-control requests one after another so commands for an app cannot race an install.
/// </summary>
public sealed partial class InstallService(
    InstallQueue queue,
    InventoryStore store,
    IOptionsMonitor<FleetOptions> fleet,
    IAdbGateway adb,
    ApkDownloader downloader,
    HomeAssistantPublisher publisher,
    InventoryTrigger trigger,
    ILogger<InstallService> logger) : BackgroundService
{
    // Progress shares in Home Assistant: download 0–50 %, upload 50–90 %, installing 90–100 %.
    private const double DownloadShare = 50;
    private const double UploadShare = 40;

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_.]*/[A-Za-z0-9_.]+$")]
    private static partial Regex LaunchComponentRegex();

    public static bool IsValidLaunchComponent(string? value) => value is not null && LaunchComponentRegex().IsMatch(value);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(request, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Unexpected error while processing {Command} for {App} on {Device}",
                    request.Command, request.AppId, request.DeviceId);
            }
            finally
            {
                queue.Complete(request);
            }
        }
    }

    private async Task ProcessAsync(InstallRequest request, CancellationToken cancellationToken)
    {
        var options = fleet.CurrentValue;
        var device = options.Devices.FirstOrDefault(d => d.Id == request.DeviceId);
        var app = device is null ? null : options.AppsFor(device).FirstOrDefault(a => a.Id == request.AppId);

        if (device is null || app is null)
        {
            logger.LogWarning("Ignoring {Command} request for unknown device/app {Device}/{App}",
                request.Command, request.DeviceId, request.AppId);
            return;
        }

        var entry = store.Find(device.Id, app.Id);
        if (entry is null)
        {
            logger.LogWarning("Ignoring {Command} request for {App} on {Device}: no inventory yet",
                request.Command, app.Id, device.Id);
            return;
        }

        if (request.Command != AppCommand.Install)
        {
            await ProcessAppCommandAsync(request.Command, device, app, entry, cancellationToken);
            return;
        }

        if (app.Policy == UpdatePolicy.Notify)
        {
            logger.LogWarning("Ignoring install request for {App}: policy is Notify", app.Id);
            return;
        }

        var update = entry.Update;
        if (!update.Installable)
        {
            logger.LogWarning("Nothing to install for {App} on {Device} (state {State})", app.Id, device.Id, update.State);
            await publisher.PublishAppStateAsync(device.Id, entry, cancellationToken);
            return;
        }

        using var activity = Telemetry.ActivitySource.StartActivity("install");
        activity?.SetTag("droidwart.device", device.Id);
        activity?.SetTag("droidwart.app", app.Id);
        activity?.SetTag("droidwart.version", update.LatestVersion);

        logger.LogInformation("Installing {App} {Version} on {Device} ({Asset}, requested by {Source})",
            app.Id, update.LatestVersion, device.Id, update.AssetName, request.Source);

        var progress = new ProgressReporter(percent =>
            publisher.PublishAppStateAsync(device.Id, entry, cancellationToken, inProgress: true, percentage: percent));
        progress.Report(0);

        try
        {
            var connection = await adb.EnsureConnectedAsync(device, cancellationToken);
            if (connection.State != ConnectionState.Online)
            {
                throw new InstallException($"Device is not connected ({connection.State}).");
            }

            var apk = await downloader.DownloadAsync(app.Id, update, p => progress.Report(p * DownloadShare / 100), cancellationToken);

            List<string> arguments = ["-r"];
            if (app.GrantPermissions)
            {
                arguments.Add("-g");
            }

            try
            {
                await adb.InstallAsync(device, apk, arguments,
                    p => progress.Report(DownloadShare + p * UploadShare / 100), cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                throw new InstallException(DescribeInstallFailure(ex.Message), ex);
            }

            progress.Report(95);

            var installed = await ReadInstalledAsync(device, entry.VariantPackage, cancellationToken)
                ?? throw new InstallException($"{entry.VariantPackage} was not found after the installation.");

            if (VersionComparer.Compare(installed.VersionName, update.LatestTag) is not 0)
            {
                throw new InstallException($"Installed version is {installed.VersionName}, expected {update.LatestVersion}.");
            }

            // Relaunch only after an update; a fresh install usually needs a first setup on the device.
            if (entry.Status == AppStatus.Installed && IsValidLaunchComponent(entry.LaunchComponent))
            {
                await adb.ShellAsync(device, $"am start -n {entry.LaunchComponent}", cancellationToken);
            }

            await progress.FlushAsync();

            var done = entry with
            {
                Status = AppStatus.Installed,
                Package = installed,
                Update = update with { State = UpdateState.UpToDate, Problem = null },
                Notes = [],
            };
            store.Replace(device.Id, done);
            await publisher.PublishAppStateAsync(device.Id, done, cancellationToken);

            logger.LogInformation("Installed {App} {Version} on {Device}", app.Id, installed.VersionName, device.Id);

            // Full refresh (launch component, other apps) in the background.
            trigger.Request();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            var reason = ex is InstallException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
            activity?.SetStatus(ActivityStatusCode.Error, reason);
            logger.LogWarning(ex, "Installing {App} on {Device} failed: {Reason}", app.Id, device.Id, reason);

            await progress.FlushAsync();
            await publisher.PublishAppStateAsync(device.Id, entry, cancellationToken, summary: $"Installation failed: {reason}");
        }
    }

    private async Task ProcessAppCommandAsync(
        AppCommand command, DeviceOptions device, AppOptions app, AppInventoryEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Status != AppStatus.Installed || entry.Package is null)
        {
            logger.LogWarning("Ignoring {Command} for {App} on {Device}: app is not installed", command, app.Id, device.Id);
            return;
        }

        var connection = await adb.EnsureConnectedAsync(device, cancellationToken);
        if (connection.State != ConnectionState.Online)
        {
            logger.LogWarning("Ignoring {Command} for {App} on {Device}: device is not connected ({State})",
                command, app.Id, device.Id, connection.State);
            return;
        }

        var package = entry.VariantPackage;
        logger.LogInformation("Executing {Command} for {App} on {Device}", command, app.Id, device.Id);

        if (command is AppCommand.Stop or AppCommand.Restart)
        {
            await RunAppShellCommandAsync(device, $"am force-stop {package}", cancellationToken);
        }

        if (command is AppCommand.Start or AppCommand.Restart)
        {
            var launch = InstallService.IsValidLaunchComponent(entry.LaunchComponent)
                ? $"am start -n {entry.LaunchComponent}"
                : $"monkey -p {package} -c android.intent.category.LAUNCHER 1";
            await RunAppShellCommandAsync(device, launch, cancellationToken);
        }

        logger.LogInformation("{Command} completed for {App} on {Device}", command, app.Id, device.Id);
    }

    private async Task RunAppShellCommandAsync(DeviceOptions device, string command, CancellationToken cancellationToken)
    {
        var result = await adb.ShellAsync(device, command, cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"ADB command '{command}' failed (exit code {result.ExitCode?.ToString() ?? "unknown"}): {result.Output}");
        }
    }

    private async Task<PackageDetails?> ReadInstalledAsync(DeviceOptions device, string package, CancellationToken cancellationToken)
    {
        // The package name comes from the validated configuration.
        var dump = await adb.ShellAsync(device, $"dumpsys package {package}", cancellationToken);
        return AndroidParsers.ParseDumpsysPackage(dump.Output, package);
    }

    /// <summary>Adds a hint for the most common package manager errors.</summary>
    public static string DescribeInstallFailure(string message)
    {
        var hint = message switch
        {
            _ when message.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE", StringComparison.Ordinal) =>
                " The installed app is signed with a different key (e.g. F-Droid vs. GitHub). Uninstall it first; its data will be lost.",
            _ when message.Contains("INSTALL_FAILED_VERSION_DOWNGRADE", StringComparison.Ordinal) =>
                " The installed version is newer than the release.",
            _ when message.Contains("INSTALL_FAILED_OLDER_SDK", StringComparison.Ordinal) =>
                " The release requires a newer Android version.",
            _ when message.Contains("INSTALL_FAILED_NO_MATCHING_ABIS", StringComparison.Ordinal) =>
                " The APK does not support the device's CPU architecture.",
            _ when message.Contains("INSTALL_FAILED_VERIFICATION_FAILURE", StringComparison.Ordinal)
                || message.Contains("INSTALL_FAILED_ABORTED", StringComparison.Ordinal) =>
                " The installation was blocked on the device, e.g. by Play Protect ('Verify apps over USB').",
            _ => "",
        };

        return message.Trim() + hint;
    }
}
