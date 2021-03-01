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
            string mqttHost = null;
            string ip = null;
            string password = null;
            var options = new OptionSet
            {
                {"m|mqttServer=", "MQTT Server", x => mqttHost = x},
                {"i|ipAddress=", "Wolf Hostname or IP address", x => ip = x},
                {"p|password=", "Wolf password", x => password = x},
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
                        await mqttClient.ConnectAsync(mqttOptions, cts.Token);
                        var client = new Ism7Client((m, c) => OnMessage(mqttClient, m, c));
                        await client.RunAsync(IPAddress.Parse(ip), password, cts.Token);
                    }
                }
            }
            catch(OperationCanceledException){}
        }

        private static Task OnMessage(IMqttClient client, MqttMessage message, CancellationToken cancellationToken)
        {
            var payload = new MqttApplicationMessageBuilder()
                .WithTopic(message.Path)
                .WithPayload(JsonConvert.SerializeObject(message.Content))
                .WithContentType("application/json")
                .Build();
            return client.PublishAsync(payload, cancellationToken);
        }
    }
}
