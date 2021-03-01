using System;
using System.Collections.Generic;
using System.Data.SqlServerCe;
using System.IO;
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
            var file = "parameter.txt";
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
                    var parameter = new List<string>();
                    using (var context = new SqlCeConnection(connectionString))
                    {
                        await context.OpenAsync(cts.Token);
                        var command = new SqlCeCommand("SELECT [ParameterId] FROM [ExchangeParameterBundleBO]", context);
                        using (var reader = await command.ExecuteReaderAsync(cts.Token))
                        {
                            while (await reader.ReadAsync(cts.Token))
                            {
                                var value = reader.GetInt64(0) / 100000;
                                parameter.Add(value.ToString());
                            }
                        }
                    }
                    File.WriteAllLines(file, parameter);
                }
            }
            catch(OperationCanceledException)
            {}
        }
    }
}
