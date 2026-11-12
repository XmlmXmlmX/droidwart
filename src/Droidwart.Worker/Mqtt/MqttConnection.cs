using Droidwart.Worker.Configuration;
using Droidwart.Worker.Installs;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace Droidwart.Worker.Mqtt;

/// <summary>
/// Keeps a single connection to the MQTT broker and reconnects automatically (MQTTnet 5 has no managed client).
/// The retained status topic is set to "online" on connect and to "offline" by the broker via Last Will.
/// </summary>
public sealed class MqttConnection : BackgroundService
{
    private const string Online = "online";
    private const string Offline = "offline";

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(15);

    private readonly MqttOptions _options;
    private readonly ILogger<MqttConnection> _logger;
    private readonly IMqttClient _client;
    private readonly InstallQueue _installQueue;
    private bool _connectFailureLogged;

    public MqttConnection(IOptions<MqttOptions> options, InstallQueue installQueue, ILogger<MqttConnection> logger)
    {
        _options = options.Value;
        _installQueue = installQueue;
        _logger = logger;
        _client = new MqttClientFactory().CreateMqttClient();
        _client.ConnectedAsync += OnConnectedAsync;
        _client.DisconnectedAsync += OnDisconnectedAsync;
        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
    }

    /// <summary>Raised after every (re)connect. Handlers publish discovery and state again.</summary>
    public event Func<CancellationToken, Task>? Connected;

    public bool IsConnected => _client.IsConnected;

    public async Task<bool> PublishAsync(string topic, string payload, bool retain, CancellationToken cancellationToken)
    {
        if (!_client.IsConnected)
        {
            return false;
        }

        try
        {
            await _client.PublishStringAsync(topic, payload, MqttQualityOfServiceLevel.AtLeastOnce, retain, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Publishing to {Topic} failed", topic);
            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("MQTT is disabled (Mqtt:Host is empty).");
            return;
        }

        var clientOptions = BuildClientOptions();
        _logger.LogInformation("MQTT enabled: broker {Host}:{Port}, user {User}, client id {ClientId}, base topic {BaseTopic}",
            _options.Host, _options.Port, string.IsNullOrWhiteSpace(_options.Username) ? "(anonymous)" : _options.Username,
            _options.ClientId, _options.BaseTopic);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_client.IsConnected)
            {
                try
                {
                    if (!_connectFailureLogged)
                    {
                        _logger.LogInformation("Connecting to MQTT broker {Host}:{Port}…", _options.Host, _options.Port);
                    }

                    // Hard upper bound in addition to the MQTTnet timeout, so a hanging TCP connect cannot block the loop.
                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    attempt.CancelAfter(TimeSpan.FromSeconds(20));
                    // MQTTnet 5 does not throw when the broker rejects the connection; it returns a result code.
                    var result = await _client.ConnectAsync(clientOptions, attempt.Token);
                    if (result.ResultCode == MqttClientConnectResultCode.Success)
                    {
                        _connectFailureLogged = false;
                    }
                    else
                    {
                        LogConnectFailure(DescribeRejection(result.ResultCode, result.ReasonString), null);
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    LogConnectFailure(ex.Message, ex);
                }
            }

            await Task.Delay(ReconnectDelay, stoppingToken);
        }
    }

    private void LogConnectFailure(string reason, Exception? exception)
    {
        // Log the first failure as a warning, repeated failures only at debug level.
        if (!_connectFailureLogged)
        {
            _logger.LogWarning("Could not connect to MQTT broker {Host}:{Port}: {Reason}. Retrying every {Delay}.",
                _options.Host, _options.Port, reason, ReconnectDelay);
            _connectFailureLogged = true;
        }
        else
        {
            _logger.LogDebug(exception, "MQTT reconnect failed: {Reason}", reason);
        }
    }

    private static string DescribeRejection(MqttClientConnectResultCode code, string? reasonString)
    {
        var hint = code switch
        {
            MqttClientConnectResultCode.BadUserNameOrPassword or MqttClientConnectResultCode.NotAuthorized =>
                " Check Mqtt:Username and the MQTT_PASSWORD of the stack.",
            MqttClientConnectResultCode.ClientIdentifierNotValid =>
                " Another client may be using the same client id.",
            _ => "",
        };

        var reason = string.IsNullOrWhiteSpace(reasonString) ? "" : $" ({reasonString})";
        return $"broker rejected the connection: {code}{reason}.{hint}";
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client.IsConnected)
        {
            // Explicit "offline" on a clean shutdown; the Last Will only covers crashes and network loss.
            await PublishAsync(_options.StatusTopic, Offline, retain: true, cancellationToken);
            await _client.DisconnectAsync(cancellationToken: cancellationToken);
        }

        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _client.Dispose();
        base.Dispose();
    }

    private MqttClientOptions BuildClientOptions()
    {
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(_options.Host, _options.Port)
            .WithClientId(_options.ClientId)
            .WithCleanSession()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithTimeout(TimeSpan.FromSeconds(10))
            .WithWillTopic(_options.StatusTopic)
            .WithWillPayload(Offline)
            .WithWillRetain()
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce);

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            builder.WithCredentials(_options.Username, _options.Password ?? "");
        }

        return builder.Build();
    }

    private Task OnConnectedAsync(MqttClientConnectedEventArgs args)
    {
        _logger.LogInformation("Connected to MQTT broker {Host}:{Port}", _options.Host, _options.Port);

        // Do not publish inside the event handler: it runs on MQTTnet's internal loop.
        _ = Task.Run(async () =>
        {
            await PublishAsync(_options.StatusTopic, Online, retain: true, CancellationToken.None);

            try
            {
                await _client.SubscribeAsync(_options.AppCommandFilter, MqttQualityOfServiceLevel.AtLeastOnce);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Subscribing to {Filter} failed; app commands will not work", _options.AppCommandFilter);
            }

            if (Connected is { } handlers)
            {
                foreach (var handler in handlers.GetInvocationList().Cast<Func<CancellationToken, Task>>())
                {
                    try
                    {
                        await handler(CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Republishing after MQTT connect failed");
                    }
                }
            }
        });

        return Task.CompletedTask;
    }

    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var message = args.ApplicationMessage;

        // Never act on retained commands: they would re-trigger an installation after every reconnect.
        if (message.Retain)
        {
            return Task.CompletedTask;
        }

        // Expected: <base>/<device>/<app>/<command>, with the payload matching the command.
        var parts = message.Topic.Split('/');
        if (parts.Length != 4 || parts[0] != _options.BaseTopic)
        {
            return Task.CompletedTask;
        }

        var payload = message.ConvertPayloadToString();
        var command = parts[3] switch
        {
            "install" when payload == MqttOptions.InstallPayload => AppCommand.Install,
            "start" when payload == "start" => AppCommand.Start,
            "stop" when payload == "stop" => AppCommand.Stop,
            "restart" when payload == "restart" => AppCommand.Restart,
            _ => (AppCommand?)null,
        };

        if (command is null)
        {
            _logger.LogWarning("Ignoring unexpected payload on {Topic}", message.Topic);
            return Task.CompletedTask;
        }

        var request = new InstallRequest(parts[1], parts[2], "Home Assistant", command.Value);
        if (_installQueue.TryEnqueue(request))
        {
            _logger.LogInformation("{Command} requested for {App} on {Device}", request.Command, request.AppId, request.DeviceId);
        }
        else
        {
            _logger.LogInformation("{Command} for {App} on {Device} is already queued or running",
                request.Command, request.AppId, request.DeviceId);
        }

        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        if (args.ClientWasConnected)
        {
            _logger.LogWarning("Disconnected from MQTT broker: {Reason}", args.ReasonString ?? args.Reason.ToString());
        }

        return Task.CompletedTask;
    }
}
