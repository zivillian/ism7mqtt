using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ism7mqtt.HomeAssistant;
using Mono.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace ism7mqtt
{
    class Program
    {
        private static bool _useSeparateTopics = false;
        private static bool _retain = false;
        private static MqttQualityOfServiceLevel _qos = MqttQualityOfServiceLevel.AtMostOnce;

        private static string _discoveryId = null;

        static async Task Main(string[] args)
        {
            bool showHelp = false;
            bool enableDebug = GetEnvBool("ISM7_DEBUG");
            string mqttHost = GetEnvString("ISM7_MQTTHOST");
            int mqttPort = GetEnvInt32("ISM7_MQTTPORT", 1883);
            string ip = GetEnvString("ISM7_IP");
            string password = GetEnvString("ISM7_PASSWORD");
            string parameter = "parameter.json";
            string mqttUsername = GetEnvString("ISM7_MQTTUSERNAME");
            string mqttPassword = GetEnvString("ISM7_MQTTPASSWORD");
            _qos = (MqttQualityOfServiceLevel)GetEnvInt32("ISM7_MQTTQOS", 0);
            _useSeparateTopics = GetEnvBool("ISM7_SEPARATE");
            _retain = GetEnvBool("ISM7_RETAIN");
            int interval = GetEnvInt32("ISM7_INTERVAL", 60);
            _discoveryId = GetEnvString("ISM7_HOMEASSISTANT_ID");
            var options = new OptionSet
            {
                {"m|mqttServer=", "MQTT Server", x => mqttHost = x},
                {"i|ipAddress=", "Wolf Hostname or IP address", x => ip = x},
                {"p|password=", "Wolf password", x => password = x},
                {"t|parameter=", $"path to parameter.json - defaults to {parameter}", x => parameter = x},
                {"mqttuser=", "MQTT username", x => mqttUsername = x},
                {"mqttpass=", "MQTT password", x => mqttPassword = x},
                {"mqttqos=", "MQTT QoS", (int x) => _qos = (MqttQualityOfServiceLevel)x},
                {"mqttport=", "MQTT port (defaults to 1883)", (int x) => mqttPort = x},
                {"s|separate", "send values to separate mqtt topics - also disables json payload", x=> _useSeparateTopics = x != null},
                {"retain", "retain mqtt messages", x=> _retain = x != null},
                {"interval=", "push interval in seconds (defaults to 60)", (int x) => interval = x},
                {"hass-id=", "HomeAssistant auto-discovery device id/entity prefix (implies --separate and --retain)", x => _discoveryId = x},
                {"d|debug", "dump raw xml messages", x => enableDebug = x != null},
                {"h|help", "show help", x => showHelp = x != null},
            };
            try
            {
                if (options.Parse(args).Count > 0)
                {
                    showHelp = true;
                }
            }
            catch (OptionException ex)
            {
                Console.Error.Write("ism7mqtt: ");
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine("Try 'ism7mqtt --help' for more information");
                return;
            }
            if (showHelp || String.IsNullOrEmpty(ip) || String.IsNullOrEmpty(mqttHost) || String.IsNullOrEmpty(password))
            {
                options.WriteOptionDescriptions(Console.Out);
                return;
            }
            if (!File.Exists(parameter))
            {
                Console.Error.WriteLine($"'{parameter}' does not exist");
                return;
            }

            if (_discoveryId != null)
            {
                _retain = true;
                _useSeparateTopics = true;
            }

            using (var cts = new CancellationTokenSource())
            {
                try
                {
                    Console.CancelKeyPress += (s, e) =>
                    {
                        cts.Cancel();
                        e.Cancel = true;
                    };
                    using (var mqttClient = new MqttFactory().CreateMqttClient())
                    {
                        var mqttOptionBuilder = new MqttClientOptionsBuilder()
                            .WithTcpServer(mqttHost, mqttPort)
                            .WithClientId($"Wolf_{ip.Replace(".", String.Empty)}")
                            .WithProtocolVersion(MqttProtocolVersion.V500);
                        if (!String.IsNullOrEmpty(mqttUsername) || !String.IsNullOrEmpty(mqttPassword))
                        {
                            mqttOptionBuilder = mqttOptionBuilder.WithCredentials(mqttUsername, mqttPassword);
                        }
                        var mqttOptions = mqttOptionBuilder.Build();
                        mqttClient.UseDisconnectedHandler(async e =>
                        {
                            Console.Error.WriteLine("mqtt disconnected - reconnecting in 5 seconds");
                            await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
                            try
                            {
                                await mqttClient.ConnectAsync(mqttOptions, cts.Token);
                            }
                            catch
                            {
                                Console.Error.WriteLine("reconnect failed");
                            }
                        });
                        await mqttClient.ConnectAsync(mqttOptions, cts.Token);
                        await mqttClient.SubscribeAsync($"Wolf/{ip}/+/set");
                        await mqttClient.SubscribeAsync($"Wolf/{ip}/+/set/#");
                        var client = new Ism7Client((config, token) => OnMessage(mqttClient, config, enableDebug, token), parameter, IPAddress.Parse(ip))
                        {
                            Interval = interval,
                            EnableDebug = enableDebug
                        };
                        mqttClient.UseApplicationMessageReceivedHandler(x => OnMessage(client, x, enableDebug, cts.Token));

                        if (_discoveryId != null)
                        {
                            client.OnInitializationFinishedAsync = (config, c) => {
                                _ = PublishDiscoveryLoop(config, mqttClient, enableDebug, c);
                                return PublishDiscoveryInfo(config, mqttClient, enableDebug, c);
                            };
                        }
                        await client.RunAsync(password, cts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    cts.Cancel();
                    throw;
                }
            }
        }

        private static string GetEnvString(string name, string defaultValue = default)
        {
            var value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
            if (value is null) return defaultValue;
            return value;
        }

        private static bool GetEnvBool(string name, bool defaultValue = default)
        {
            var value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
            if (value is null) return defaultValue;
            if (Boolean.TryParse(value, out var parsed))
                return  parsed;
            return defaultValue;
        }

        private static int GetEnvInt32(string name, int defaultValue = default)
        {
            var value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
            if (value is null) return defaultValue;
            if (Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            return defaultValue;
        }

        private static async Task PublishDiscoveryLoop(Ism7Config config, IMqttClient mqttClient, bool debug, CancellationToken cancellationToken)
        {
            while (true)
            {
                await Task.Delay(1700000, cancellationToken);
                await PublishDiscoveryInfo(config, mqttClient, debug, cancellationToken);
            }
        }

        private static async Task PublishDiscoveryInfo(Ism7Config config, IMqttClient mqttClient, bool debug, CancellationToken cancellationToken)
        {
            var discovery = new HaDiscovery(config) { EnableDebug = debug };
            foreach (var message in discovery.GetDiscoveryInfo(_discoveryId))
            {
                var data = JsonSerializer.Serialize(message.Content);
                // In order for removed devices to eventually disappear from HA, we expire the message after 1800s (30 minutes),
                // but also re-send the messages every 1700s
                var builder = new MqttApplicationMessageBuilder()
                    .WithTopic(message.Path)
                    .WithPayload(data)
                    .WithContentType("application/json")
                    .WithRetainFlag()
                    .WithMessageExpiryInterval(1800)
                    .WithQualityOfServiceLevel(_qos);
                var payload = builder
                    .Build();
                await mqttClient.PublishAsync(payload, cancellationToken);
            }
        }

        private static Task OnMessage(Ism7Client client, MqttApplicationMessageReceivedEventArgs arg, bool debug, CancellationToken cancellationToken)
        {
            var message = arg.ApplicationMessage;

            JsonObject data;
            string topic;
            if (debug)
            {
                Console.WriteLine($"received mqtt with topic '{message.Topic}' '{message.ConvertPayloadToString()}'");
            }
            if (message.Topic.EndsWith("/set"))
            {
                //json
                data = JsonSerializer.Deserialize<JsonObject>(message.ConvertPayloadToString());
                topic = message.Topic.Substring(0, message.Topic.Length - 4);
                return client.OnCommandAsync(topic, data, cancellationToken);
            }
            else
            {
                //single value
                var index = message.Topic.LastIndexOf("/set/");
                var property = message.Topic.Substring(index + 5);
                topic = message.Topic.Substring(0, index);
                var parts = property.Split('/');
                return client.OnCommandAsync(topic, parts, message.ConvertPayloadToString(), cancellationToken);
            }
        }

        private static async Task OnMessage(IMqttClient client, Ism7Config config, bool debug, CancellationToken cancellationToken)
        {
            if (!client.IsConnected)
            {
                if (debug)
                {
                    Console.WriteLine("not connected - skipping mqtt publish");
                }
                return;
            }
            if (!_useSeparateTopics)
            {
                var deviceMessages = config.JsonMessages;
                foreach (var message in deviceMessages)
                {
                    var data = JsonSerializer.Serialize(message.Content);
                    var builder = new MqttApplicationMessageBuilder()
                        .WithTopic(message.Path)
                        .WithPayload(data)
                        .WithContentType("application/json")
                        .WithQualityOfServiceLevel(_qos);
                    if (_retain)
                        builder = builder.WithRetainFlag();
                    var payload = builder
                        .Build();
                    if (debug)
                    {
                        Console.WriteLine($"publishing mqtt with topic '{message.Path}' '{data}'");
                    }
                    await client.PublishAsync(payload, cancellationToken);
                }
            }
            else
            {
                var messages = config.MqttMessages;
                foreach (var message in messages)
                {
                    var topic = message.Path;
                    var builder = new MqttApplicationMessageBuilder()
                        .WithTopic(topic)
                        .WithPayload(message.Content)
                        .WithQualityOfServiceLevel(_qos);
                    if (_retain)
                        builder = builder.WithRetainFlag();
                    var payload = builder.Build();
                    if (debug)
                    {
                        Console.WriteLine($"publishing mqtt with topic '{topic}' '{message.Content:X2}'");
                    }
                    await client.PublishAsync(payload, cancellationToken);
                }
            }
        }
    }
}
