using Droidwart.Worker.Configuration;

namespace Droidwart.Worker.Adb;

public enum ConnectionState
{
    Online,
    Unauthorized,
    Offline,
    Unreachable,
}

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
    Task<ConnectionState> EnsureConnectedAsync(DeviceOptions device, CancellationToken cancellationToken);

    /// <summary>Runs a command in the device shell (as the ADB shell user, without root).</summary>
    Task<ShellResult> ShellAsync(DeviceOptions device, string command, CancellationToken cancellationToken);
}
