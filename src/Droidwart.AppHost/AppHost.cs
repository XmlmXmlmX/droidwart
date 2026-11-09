var builder = DistributedApplication.CreateBuilder(args);

var worker = builder.AddProject<Projects.Droidwart_Worker>("droidwart")
    .WithEnvironment("Storage__DataDirectory", Path.Combine(builder.AppHostDirectory, "..", "..", "data"));

// Variante 1 (Standard): eigener ADB-Server als Container, gebaut aus docker/adb-server – wie im Portainer-Stack.
// Achtung: Das Volume hat ein eigenes Schlüsselpaar, das einmalig auf den Phones freigegeben werden muss.
//
// Variante 2: bestehenden ADB-Server nutzen (z. B. den im Portainer-LXC oder lokales adb), per User Secret:
//   dotnet user-secrets set "Droidwart:ExternalAdbServer" "192.168.178.x:5037" --project src/Droidwart.AppHost
var externalAdb = builder.Configuration["Droidwart:ExternalAdbServer"];

if (string.IsNullOrWhiteSpace(externalAdb))
{
    var adb = builder.AddDockerfile("adb-server", Path.Combine("..", "..", "docker", "adb-server"))
        .WithEndpoint(targetPort: 5037, name: "adb", scheme: "tcp")
        .WithVolume("droidwart-dev-adb-keys", "/home/adb/.android")
        // Container überlebt Neustarts des AppHost: Verbindungen und Schlüssel bleiben erhalten.
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

// Ab Phase 3: MQTT-Zugangsdaten als Aspire-Parameter (landen in den User Secrets des AppHost).
// var mqttPassword = builder.AddParameter("mqtt-password", secret: true);
// worker.WithEnvironment("Mqtt__Host", "192.168.178.157")
//       .WithEnvironment("Mqtt__Username", "droidwart")
//       .WithEnvironment("Mqtt__Password", mqttPassword);

builder.Build().Run();
