namespace Droidwart.Worker.Configuration;

public enum UpdatePolicy
{
    /// <summary>Nur anzeigen, Installation über Droidwart gesperrt.</summary>
    Notify,

    /// <summary>Installation nur per Klick in Home Assistant.</summary>
    Manual,

    /// <summary>Installation automatisch im Wartungsfenster (Canary zuerst).</summary>
    Auto,
}

public sealed class AppOptions
{
    /// <summary>Technische Id, z. B. "ha-companion". Wird später Teil der MQTT-Topics.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>GitHub-Repository im Format "owner/name".</summary>
    public string Repo { get; set; } = "";

    /// <summary>Erwarteter Paketname. Leer lassen, wenn noch unbekannt – das Inventar listet dann Kandidaten.</summary>
    public string? Package { get; set; }

    /// <summary>Weitere Paketnamen derselben App (z. B. Flavors wie "minimal" oder "fdroid").</summary>
    public List<string> AlternativePackages { get; set; } = new();

    /// <summary>Regex für das Release-Asset. Platzhalter {abi} wird je Gerät ersetzt.</summary>
    public string AssetPattern { get; set; } = @"\.apk$";

    public bool IncludePrereleases { get; set; }

    public UpdatePolicy Policy { get; set; } = UpdatePolicy.Manual;

    /// <summary>Wenn gesetzt (z. B. "github"), wird gewarnt, falls die App aus einem Store stammt.</summary>
    public string? RequireInstallSource { get; set; }

    /// <summary>Komponente zum Neustart nach einem Update ("paket/.Activity"). Leer = automatisch ermitteln.</summary>
    public string? LaunchComponent { get; set; }

    /// <summary>Hinweis, der im Inventar erscheint, wenn die App nicht gefunden wird.</summary>
    public string? NotInstalledHint { get; set; }

    /// <summary>Geräte-Ids, auf denen die App verwaltet wird. Leer = alle Geräte.</summary>
    public List<string> Devices { get; set; } = new();
}
