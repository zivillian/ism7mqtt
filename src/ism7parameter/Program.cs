using System;
using System.Collections.Generic;
using System.Data.SqlServerCe;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Mono.Options;

namespace ism7parameter
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var path = @"%APPDATA%\Wolf GmbH\Smartset\App_Data\smartsetpc.sdf";
            var file = "parameter.json";
            var showHelp = false;
            var options = new OptionSet
            {
                {"f|file=", $"Path to smartsetpc.sdf - defaults to {path}", x => path = x},
                {"t|target=", $"Target filename - defaults to {file}", x => file = x},
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
                Console.Error.Write("ism7parameter: ");
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine("Try 'ism7parameter --help' for more information");
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
                        e.Cancel = true;
                        cts.Cancel();
                    };
                    string connectionString = $"Data Source={Environment.ExpandEnvironmentVariables(path)};Password=\"!!#()*6LauÃÿ\"";
                    var parameters = new List<Parameter>();
                    var devices = new List<Device>();
                    using (var context = new SqlCeConnection(connectionString))
                    {
                        await context.OpenAsync(cts.Token);
                        var command = new SqlCeCommand("SELECT [DeviceId], [ParameterId] FROM [ExchangeParameterBundleBO]", context);
                        using (var reader = await command.ExecuteReaderAsync(cts.Token))
                        {
                            while (await reader.ReadAsync(cts.Token))
                            {
                                var parameter = new Parameter
                                {
                                    DeviceId = reader.GetInt64(0),
                                    ParameterId = reader.GetInt64(1) / 100_000
                                };
                                parameters.Add(parameter);
                            }
                        }
                        command = new SqlCeCommand("SELECT [Id], [ReadBusAddress], [DeviceTemplateId] FROM [DeviceBo] WHERE [IsVisible] = 1", context);
                        using (var reader = await command.ExecuteReaderAsync(cts.Token))
                        {
                            while (await reader.ReadAsync(cts.Token))
                            {
                                var device = new Device
                                {
                                    Id = reader.GetInt64(0),
                                    ReadBusAddress = reader.GetString(1),
                                    DeviceTemplateId = reader.GetInt32(2),
                                };
                                device.Parameters = parameters.Where(x => x.DeviceId == device.Id).ToList();
                                devices.Add(device);
                            }
                        }
                    }
                    File.WriteAllText(file, JsonSerializer.Serialize(new Config{Devices = devices}, new JsonSerializerOptions{WriteIndented = true}));
                }
            }
            catch(OperationCanceledException)
            {}
        }

        class Config
        {
            public List<Device> Devices { get; set; }
        }

        class Device
        {
            [JsonIgnore]
            public long Id { get; set; }
            
            public string ReadBusAddress { get; set; }
            
            public int DeviceTemplateId { get; set; }
            
            [JsonIgnore]
            public List<Parameter> Parameters { get; set; }

            [JsonPropertyName("Parameter")]
            public IEnumerable<long> Ids
            {
                get { return Parameters.Select(x => x.ParameterId); }
            }
        }

        class Parameter
        {
            [JsonIgnore]
            public long DeviceId { get; set; }

            public long ParameterId { get; set; }
        }

        public static void DecryptSystemConfig()
        {
            var pass = "92(*l´ß";
            var bytes = Encoding.UTF8.GetBytes(pass);
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(bytes);
                var files = Directory.GetFiles(Environment.ExpandEnvironmentVariables(@"%APPDATA%\Wolf GmbH\Smartset\system-config\"), "*.dat");
                foreach (var file in files)
                {
                    using (var rijndael = Rijndael.Create())
                    {
                        rijndael.Mode = CipherMode.CBC;
                        rijndael.Padding = PaddingMode.PKCS7;
                        var password = Convert.FromBase64String("z0VCAYPN8aY0i1kehH/2jZgm2IbYpe7mH9NtdH5dQ+A=");
                        Rfc2898DeriveBytes rfc2898DeriveBytes = new Rfc2898DeriveBytes(password, hash, 1000);
                        var key = rfc2898DeriveBytes.GetBytes(rijndael.KeySize / 8);
                        var iv = rfc2898DeriveBytes.GetBytes(rijndael.BlockSize / 8);
                        var decryptor = rijndael.CreateDecryptor(key, iv);
                        var xml = Path.Combine(Path.GetDirectoryName(file), Path.GetFileNameWithoutExtension(file) + ".xml");
                        using (var fs = File.OpenRead(file))
                        using (var decrypt = new CryptoStream(fs, decryptor, CryptoStreamMode.Read))
                        using (var target = File.OpenWrite(xml))
                        {
                            decrypt.CopyTo(target);
                        }
                    }
                }
            }
        }
    }
}
