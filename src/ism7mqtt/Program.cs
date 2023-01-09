using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Mono.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;

namespace ism7mqtt
{
    class Program
    {
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
                {"s|separate", "send values to separate mqtt topics - also disables json payload", x=> _useSeparateTopics = x != null},
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
                        await mqttClient.SubscribeAsync($"Wolf/{ip}/+/set/#");
                        var client = new Ism7Client((config, token) => OnMessage(mqttClient, config, enableDebug, token), parameter, IPAddress.Parse(ip))
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
            }
            else
            {
                var messages = config.MqttMessages;
                foreach (var message in messages)
                {
                    var topic = message.Path;
                    var builder = new MqttApplicationMessageBuilder().WithTopic(topic)
                        .WithPayload(message.Content);
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
