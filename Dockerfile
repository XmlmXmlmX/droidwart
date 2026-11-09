# Droidwart worker
# Until the .NET 11 GA release, build with --build-arg DOTNET_TAG=11.0-preview
ARG DOTNET_TAG=11.0
ARG VERSION=0.0.0-dev

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_TAG} AS build
ARG VERSION
WORKDIR /src

COPY Directory.Build.props ./
COPY src/Droidwart.ServiceDefaults/Droidwart.ServiceDefaults.csproj src/Droidwart.ServiceDefaults/
COPY src/Droidwart.Worker/Droidwart.Worker.csproj src/Droidwart.Worker/
RUN dotnet restore src/Droidwart.Worker/Droidwart.Worker.csproj

COPY src/ src/
RUN dotnet publish src/Droidwart.Worker/Droidwart.Worker.csproj -c Release -o /app --no-restore -p:Version=${VERSION}

FROM mcr.microsoft.com/dotnet/runtime:${DOTNET_TAG}
WORKDIR /app

# Prepare the data directory so a named volume inherits the app user's permissions
RUN mkdir -p /data && chown "$APP_UID" /data

COPY --from=build /app .

ENV DOTNET_ENVIRONMENT=Production \
    Storage__DataDirectory=/data

VOLUME ["/data"]
USER $APP_UID

ENTRYPOINT ["dotnet", "Droidwart.Worker.dll"]
