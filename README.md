# Droidwart

Self-hosted .NET worker that keeps rooted Android devices up to date via ADB. Tracks GitHub releases
and exposes app updates as native Home Assistant update entities over MQTT. Built for old phones
repurposed as CCTV cameras.

Status: **Phase 3 – Home Assistant** (inventory and release check, published via MQTT discovery as update entities plus an ADB connectivity sensor per device).

## Requirements

- .NET 11 SDK
- Docker Desktop (for the ADB server container in the Aspire development environment)
- Optional: Aspire CLI (`aspire run`)

## Development with Aspire

    aspire run
    # or: dotnet run --project src/Droidwart.AppHost

The Aspire dashboard shows the worker, the ADB server container, logs, traces and metrics.
The ADB server container has its own key pair (volume `droidwart-dev-adb-keys`), which must be
authorized once on the phones. Alternatively, use an existing ADB server:

    dotnet user-secrets set "Droidwart:ExternalAdbServer" "<host>:5037" --project src/Droidwart.AppHost

Inventory output: logs in the dashboard and `data/inventory.json` in the repository directory.

## Without Aspire

    cp src/Droidwart.Worker/appsettings.Development.json.example src/Droidwart.Worker/appsettings.Development.json
    dotnet run --project src/Droidwart.Worker -- --RunOnce=true

## Tests

    dotnet test

## Images

    docker build -t ghcr.io/xmlmxmlmx/droidwart:dev --build-arg DOTNET_TAG=11.0-preview .
    docker build -t ghcr.io/xmlmxmlmx/droidwart-adb-server:dev docker/adb-server

Deployment as a Portainer stack: `deploy/docker-compose.yml`.

## Structure

- `src/Droidwart.AppHost` – Aspire AppHost for local development
- `src/Droidwart.ServiceDefaults` – OpenTelemetry and HTTP resilience
- `src/Droidwart.Worker` – worker service (.NET 11)
- `tests/Droidwart.Tests` – unit tests (xUnit v3)
- `docker/adb-server` – ADB server image (Debian, adb, tini)
- `deploy/docker-compose.yml` – Portainer stack

## License

MIT

## CI

`.github/workflows/build.yml` runs the tests and publishes both images to GHCR:
`edge` for every push to `main`, `x.y.z` and `x.y` for tags like `v1.0.0`.
