namespace Droidwart.Worker.Installs;

/// <summary>An installation step failed for a known reason; the message is shown in Home Assistant.</summary>
public sealed class InstallException(string message, Exception? inner = null) : Exception(message, inner);
