var builder = DistributedApplication.CreateBuilder(args);

var worker = builder.AddProject<Projects.Droidwart_Worker>("droidwart")
    .WithEnvironment("Storage__DataDirectory", Path.Combine(builder.AppHostDirectory, "..", "..", "data"));

// Option 1 (default): a dedicated ADB server container, built from docker/adb-server, same as in the Portainer stack.
// Note: its volume holds its own key pair, which must be authorized once on the phones.
//
// Option 2: use an existing ADB server (e.g. the one in the Portainer LXC or a local adb) via user secret:
//   dotnet user-secrets set "Droidwart:ExternalAdbServer" "192.168.178.x:5037" --project src/Droidwart.AppHost
var externalAdb = builder.Configuration["Droidwart:ExternalAdbServer"];

if (string.IsNullOrWhiteSpace(externalAdb))
{
    var adb = builder.AddDockerfile("adb-server", Path.Combine("..", "..", "docker", "adb-server"))
        .WithEndpoint(targetPort: 5037, name: "adb", scheme: "tcp")
        .WithVolume("droidwart-dev-adb-keys", "/home/adb/.android")
        // The container survives AppHost restarts: connections and keys are preserved.
        .WithLifetime(ContainerLifetime.Persistent);

    var adbEndpoint = adb.GetEndpoint("adb");

    worker
        .WithEnvironment("Adb__ServerHost", $"{adbEndpoint.Property(EndpointProperty.Host)}")
        .WithEnvironment("Adb__ServerPort", $"{adbEndpoint.Property(EndpointProperty.Port)}")
        .WaitFor(adb);
}
else
{
    var parts = externalAdb.Split(':', 2);
    worker
        .WithEnvironment("Adb__ServerHost", parts[0])
        .WithEnvironment("Adb__ServerPort", parts.Length > 1 ? parts[1] : "5037");
}

// MQTT: only enabled if a broker is configured, e.g.
//   dotnet user-secrets set "Droidwart:MqttHost" "192.168.178.157" --project src/Droidwart.AppHost
//   dotnet user-secrets set "Parameters:mqtt-password" "<password>" --project src/Droidwart.AppHost
// Use a separate MQTT client id for development, otherwise the broker disconnects the production instance.
var mqttHost = builder.Configuration["Droidwart:MqttHost"];
if (!string.IsNullOrWhiteSpace(mqttHost))
{
    var mqttPassword = builder.AddParameter("mqtt-password", secret: true);
    worker
        .WithEnvironment("Mqtt__Host", mqttHost)
        .WithEnvironment("Mqtt__Username", "droidwart-mqtt")
        .WithEnvironment("Mqtt__Password", mqttPassword)
        .WithEnvironment("Mqtt__ClientId", "droidwart-dev")
        .WithEnvironment("Mqtt__BaseTopic", "droidwart-dev");
}

builder.Build().Run();
