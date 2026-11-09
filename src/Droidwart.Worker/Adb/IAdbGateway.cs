using Droidwart.Worker.Configuration;

namespace Droidwart.Worker.Adb;

public enum ConnectionState
{
    Online,
    Unauthorized,
    Offline,
    Unreachable,
}

/// <summary>Ergebnis eines Shell-Befehls. ExitCode ist null, wenn er nicht ermittelt werden konnte.</summary>
public sealed record ShellResult(string Output, int? ExitCode)
{
    public bool Success => ExitCode == 0;
}

/// <summary>Der ADB-Server selbst (Port 5037) ist nicht erreichbar.</summary>
public sealed class AdbServerUnavailableException(string message, Exception inner) : Exception(message, inner);

/// <summary>Kapselt alle ADB-Zugriffe, damit der Rest des Codes testbar bleibt.</summary>
public interface IAdbGateway
{
    /// <summary>Stellt sicher, dass das Gerät am ADB-Server verbunden ist.</summary>
    Task<ConnectionState> EnsureConnectedAsync(DeviceOptions device, CancellationToken cancellationToken);

    /// <summary>Führt einen Befehl in der Shell des Geräts aus (als ADB-Shell-User, ohne Root).</summary>
    Task<ShellResult> ShellAsync(DeviceOptions device, string command, CancellationToken cancellationToken);
}
