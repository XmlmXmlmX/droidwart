using Droidwart.Worker.Configuration;

namespace Droidwart.Worker.Adb;

public enum ConnectionState
{
    Online,
    Unauthorized,
    Offline,
    Unreachable,
}

/// <summary>Connection state plus the ADB server's response if connecting failed.</summary>
public sealed record ConnectionResult(ConnectionState State, string? Detail = null);

/// <summary>Result of a shell command. ExitCode is null if it could not be determined.</summary>
public sealed record ShellResult(string Output, int? ExitCode)
{
    public bool Success => ExitCode == 0;
}

/// <summary>The ADB server itself (port 5037) is not reachable.</summary>
public sealed class AdbServerUnavailableException(string message, Exception inner) : Exception(message, inner);

/// <summary>Encapsulates all ADB access so the rest of the code stays testable.</summary>
public interface IAdbGateway
{
    /// <summary>Ensures the device is connected to the ADB server.</summary>
    Task<ConnectionResult> EnsureConnectedAsync(DeviceOptions device, CancellationToken cancellationToken);

    /// <summary>Runs a command in the device shell (as the ADB shell user, without root).</summary>
    Task<ShellResult> ShellAsync(DeviceOptions device, string command, CancellationToken cancellationToken);

    /// <summary>
    /// Streams an APK to the device and installs it ("cmd package install").
    /// <paramref name="uploadProgress"/> receives 0–100 while the APK is transferred.
    /// Throws <see cref="InvalidOperationException"/> with the package manager's message if the installation fails.
    /// </summary>
    Task InstallAsync(DeviceOptions device, string apkPath, IReadOnlyList<string> arguments, Action<double> uploadProgress,
        CancellationToken cancellationToken);
}
