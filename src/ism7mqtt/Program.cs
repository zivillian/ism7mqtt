using System;
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

        static async Task Main(string[] args)
        {
            bool showHelp = false;
            bool enableDebug = false;
            string mqttHost = null;
            string ip = null;
            string password = null;
            string parameter = "parameter.json";
            string mqttUsername = null;
            string mqttPassword = null;
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
                            EnableDebug = enableDebug
                        };
                        mqttClient.UseApplicationMessageReceivedHandler(x => OnMessage(client, x, cts.Token));
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

        private static Task OnMessage(Ism7Client client, MqttApplicationMessageReceivedEventArgs arg, CancellationToken cancellationToken)
        {
            var message = arg.ApplicationMessage;

            JObject data;
            string topic;
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
                var payload = new MqttApplicationMessageBuilder()
                    .WithTopic(message.Path)
                    .WithPayload(data)
                    .WithContentType("application/json")
                    .Build();
                if (debug)
                {
                    Console.WriteLine($"publishing mqtt with topic '{message.Path}' '{data}'");
                }
                await client.PublishAsync(payload, cancellationToken);
            }
            if (_useSeparateTopics)
            {
                var builder = new MqttApplicationMessageBuilder();
                foreach (var property in message.Content.Properties())
                {
                    var name = EscapeMqttTopic(property.Name);
                    var data = property.Value.ToString();
                    var topic = $"{message.Path}/{name}";
                    var payload = builder.WithTopic(topic)
                        .WithPayload(data)
                        .Build();
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
