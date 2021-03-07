using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mono.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;
using Newtonsoft.Json;

namespace ism7mqtt
{
    class Program
    {
        static async Task Main(string[] args)
        {
            bool showHelp = false;
            bool enableDebug = false;
            string mqttHost = null;
            string ip = null;
            string password = null;
            var options = new OptionSet
            {
                {"m|mqttServer=", "MQTT Server", x => mqttHost = x},
                {"i|ipAddress=", "Wolf Hostname or IP address", x => ip = x},
                {"p|password=", "Wolf password", x => password = x},
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
            if (showHelp)
            {
                options.WriteOptionDescriptions(Console.Out);
                return;
            }
            try
            {
                using (var cts = new CancellationTokenSource())
                {
                    Console.CancelKeyPress += (s, e) =>
                    {
                        cts.Cancel();
                        e.Cancel = true;
                    };
                    using (var mqttClient = new MqttFactory().CreateMqttClient())
                    {
                        var mqttOptions = new MqttClientOptionsBuilder()
                            .WithTcpServer(mqttHost)
                            .WithClientId($"Wolf_{ip.Replace(".", String.Empty)}")
                            .Build();
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
                        var client = new Ism7Client((m, c) => OnMessage(mqttClient, m, enableDebug, c));
                        client.EnableDebug = enableDebug;
                        await client.RunAsync(IPAddress.Parse(ip), password, cts.Token);
                    }
                }
            }
            catch(OperationCanceledException){}
        }

        private static Task OnMessage(IMqttClient client, MqttMessage message, bool debug, CancellationToken cancellationToken)
        {
            if (!client.IsConnected)
            {
                if (debug)
                {
                    Console.WriteLine("not connected - skipping mqtt publish");
                }
                return Task.CompletedTask;
            }
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
            return client.PublishAsync(payload, cancellationToken);
        }
    }
}
