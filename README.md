# Droidwart

Self-hosted .NET worker that keeps rooted Android devices up to date via ADB. Tracks GitHub releases
and exposes app updates as native Home Assistant update entities over MQTT. Built for old phones
repurposed as CCTV cameras.

Stand: **Phase 1 – Inventar** (Geräteinfos, installierte Versionen, Installationsquelle, Start-Komponenten).

## Voraussetzungen

- .NET 11 SDK
- Docker Desktop (für den ADB-Server-Container in der Aspire-Entwicklungsumgebung)
- Optional: Aspire CLI (`aspire run`)

## Entwicklung mit Aspire

    aspire run
    # oder: dotnet run --project src/Droidwart.AppHost

Das Aspire-Dashboard zeigt Worker, ADB-Server-Container, Logs, Traces und Metriken.
Der ADB-Server-Container hat ein eigenes Schlüsselpaar (Volume `droidwart-dev-adb-keys`), das einmalig
auf den Phones freigegeben werden muss. Alternativ einen bestehenden ADB-Server verwenden:

    dotnet user-secrets set "Droidwart:ExternalAdbServer" "<host>:5037" --project src/Droidwart.AppHost

Inventar-Ergebnis: Log im Dashboard und `data/inventory.json` im Repository-Verzeichnis.

## Ohne Aspire

    cp src/Droidwart.Worker/appsettings.Development.json.example src/Droidwart.Worker/appsettings.Development.json
    dotnet run --project src/Droidwart.Worker -- --RunOnce=true

## Tests

    dotnet test

## Images

    docker build -t ghcr.io/xmlmxmlmx/droidwart:dev .
    docker build -t ghcr.io/xmlmxmlmx/droidwart-adb-server:dev docker/adb-server

Deployment als Portainer-Stack: `deploy/docker-compose.yml`.

## Aufbau

- `src/Droidwart.AppHost` – Aspire-AppHost für die lokale Entwicklung
- `src/Droidwart.ServiceDefaults` – OpenTelemetry und HTTP-Resilienz
- `src/Droidwart.Worker` – Worker-Service (.NET 11)
- `tests/Droidwart.Tests` – Unit-Tests (xUnit v3)
- `docker/adb-server` – ADB-Server-Image (Debian, adb, tini)
- `deploy/docker-compose.yml` – Portainer-Stack

## Lizenz

MIT
