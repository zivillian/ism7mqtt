using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Mono.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ism7mqtt
{
    class Program
    {
        private static bool _disableJson = false;
        private static bool _useSeparateTopics = false;
        private static bool _retain = false;

        static async Task Main(string[] args)
        {
            bool showHelp = false;
            bool enableDebug = GetEnvBool("ISM7_DEBUG");
            string mqttHost = GetEnvString("ISM7_MQTTHOST");
            string ip = GetEnvString("ISM7_IP");
            string password = GetEnvString("ISM7_PASSWORD");
            string parameter = "parameter.json";
            string mqttUsername = GetEnvString("ISM7_MQTTUSERNAME");
            string mqttPassword = GetEnvString("ISM7_MQTTPASSWORD");
            _disableJson = GetEnvBool("ISM7_DISABLEJSON");
            _useSeparateTopics = GetEnvBool("ISM7_SEPARATE");
            _retain = GetEnvBool("ISM7_RETAIN");
            int interval = GetEnvInt32("ISM7_INTERVAL", 60);
            var options = new OptionSet
            {
                {"m|mqttServer=", "MQTT Server", x => mqttHost = x},
                {"i|ipAddress=", "Wolf Hostname or IP address", x => ip = x},
                {"p|password=", "Wolf password", x => password = x},
                {"t|parameter=", $"path to parameter.json - defaults to {parameter}", x => parameter = x},
                {"mqttuser=", "MQTT username", x => mqttUsername = x},
                {"mqttpass=", "MQTT password", x => mqttPassword = x},
                {"s|separate", "send values to separate mqtt topics", x=> _useSeparateTopics = x != null},
                {"disable-json", "disable json mqtt payload", x=> _disableJson = x != null},
                {"retain", "retain mqtt messages", x=> _retain = x != null},
                {"interval=", "push interval in seconds (defaults to 60)", (int x) => interval = x},
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
            if (showHelp || ip is null || mqttHost is null || password is null)
            {
                options.WriteOptionDescriptions(Console.Out);
                return;
            }
            if (!File.Exists(parameter))
            {
                Console.Error.WriteLine($"'{parameter}' does not exist");
                return;
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
                            .WithTcpServer(mqttHost)
                            .WithClientId($"Wolf_{ip.Replace(".", String.Empty)}");
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
                        await mqttClient.SubscribeAsync($"Wolf/{ip}/+/set/+");
                        var client = new Ism7Client((m, c) => OnMessage(mqttClient, m, enableDebug, c), parameter, IPAddress.Parse(ip))
                        {
                            Interval = interval,
                            EnableDebug = enableDebug
                        };
                        mqttClient.UseApplicationMessageReceivedHandler(x => OnMessage(client, x, enableDebug, cts.Token));
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
            var parsed = new BooleanConverter().ConvertFromString(value);
            if (parsed is null) return defaultValue;
            return (bool)parsed;
        }

        private static int GetEnvInt32(string name, int defaultValue = default)
        {
            var value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
            if (value is null) return defaultValue;
            var parsed = new Int32Converter().ConvertFromString(value);
            if (parsed is null) return defaultValue;
            return (int)parsed;
        }

        private static Task OnMessage(Ism7Client client, MqttApplicationMessageReceivedEventArgs arg, bool debug, CancellationToken cancellationToken)
        {
            var message = arg.ApplicationMessage;

            JObject data;
            string topic;
            if (debug)
            {
                Console.WriteLine($"received mqtt with topic '{message.Topic}' '{message.ConvertPayloadToString()}'");
            }
            if (message.Topic.EndsWith("/set"))
            {
                //json
                data = JObject.Parse(message.ConvertPayloadToString());
                topic = message.Topic.Substring(0, message.Topic.Length - 4);
            }
            else
            {
                //single value
                var index = message.Topic.LastIndexOf('/');
                var property = message.Topic.Substring(index + 1);
                topic = message.Topic.Substring(0, index - 4);
                data = new JObject{{property, new JValue(message.ConvertPayloadToString())}};
            }
            return client.OnCommandAsync(topic, data, cancellationToken);
        }

        private static async Task OnMessage(IMqttClient client, MqttMessage message, bool debug, CancellationToken cancellationToken)
        {
            if (!client.IsConnected)
            {
                if (debug)
                {
                    Console.WriteLine("not connected - skipping mqtt publish");
                }
                return;
            }
            if (!_disableJson)
            {
                var data = JsonConvert.SerializeObject(message.Content);
                var builder = new MqttApplicationMessageBuilder()
                    .WithTopic(message.Path)
                    .WithPayload(data)
                    .WithContentType("application/json");
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
            if (_useSeparateTopics)
            {
                foreach (var property in message.Content.Properties())
                {
                    var name = EscapeMqttTopic(property.Name);
                    var data = JsonConvert.SerializeObject(property.Value);
                    var topic = $"{message.Path}/{name}";
                    var builder = new MqttApplicationMessageBuilder().WithTopic(topic)
                        .WithPayload(data);
                    if (_retain)
                        builder = builder.WithRetainFlag();
                    var payload = builder.Build();
                    if (debug)
                    {
                        Console.WriteLine($"publishing mqtt with topic '{topic}' '{data}'");
                    }
                    await client.PublishAsync(payload, cancellationToken);
                }
            }
        }

        private static string EscapeMqttTopic(string name)
        {
            return name.Replace('/', '_').Replace(' ', '_');
        }
    }
}
