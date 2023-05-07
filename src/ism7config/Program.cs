using System.Net;
using System.Text.Json;
using CommonServiceLocator;
using LuCon.Common.ConditionService;
using LuCon.Common.ConfigService;
using LuCon.Common.PortalModel;
using LuCon.LocalConnection;
using LuCon.Mobile.Data;
using LuCon.SocketServer.GatewayInterface;
using LuCon.SocketServer.SocketServerBase;
using LuCon.WebPortal.StandaloneService;
using Microsoft.Extensions.Logging.Abstractions;
using Mono.Options;
using Wolf.Common.Busconfig;
using Wolf.SocketServer.ISMInterface;
using LogLevel = NLog.LogLevel;

namespace ism7config;

internal class Program
{
    static async Task Main(string[] args)
    {
        var file = "parameter.json";
        string password = GetEnvString("ISM7_PASSWORD");
        string ip = GetEnvString("ISM7_IP");
        var showHelp = false;
        var options = new OptionSet
        {
            {"i|ipAddress=", "Wolf Hostname or IP address", x => ip = x},
            {"p|password=", "Wolf password", x => password = x},
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
            Console.Error.Write("ism7config: ");
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Try 'ism7config --help' for more information");
            return;
        }
        if (showHelp || String.IsNullOrEmpty(ip) || String.IsNullOrEmpty(password))
        {
            options.WriteOptionDescriptions(Console.Out);
            return;
        }

        var logconfig = new NLog.Config.LoggingConfiguration();
        var logconsole = new NLog.Targets.ConsoleTarget("logconsole");
        logconfig.AddRule(LogLevel.Trace, LogLevel.Fatal, logconsole);
        NLog.LogManager.Configuration = logconfig;

        var unitOfWork = new InMemoryDatabase();
        var textService = new TextService();
        var nc = new NetworkConnector(unitOfWork, textService, new NetworkConnectorSettings{TcpClientConnectTimeoutMs = 60000}, new BusconfigPendingGatewaysSingleton());
        var streamHandler = new XplatStreamHandler();

        var gw = new GatewayBO
        {
            GatewayUserName = String.Empty,
            IsLocked = false,
            ConnectCounter = 0
        };
        var configPath = "invalid";
        var fileSystemSettings = new FileSystemSettings
        {
            ConfigPath = configPath,
            UseEncryption = false
        };
        var converterTemplateService = new ConverterTemplateServiceImplISM(fileSystemSettings, _ => new MemoryStream(Resources.ConverterTemplates));
        var deviceTemplateService = new DeviceTemplateServiceImplISM(fileSystemSettings, _ => new MemoryStream(Resources.DeviceTemplates));
        var nullLoggerFactory = new NullLoggerFactory();
        var bundleQueueWorker = new BundleQueueWorker(nullLoggerFactory,
            new TelegrBundleRequestFactory(new TelegrBundleRequestFactorySettings(), deviceTemplateService),
            new BundleQueueWorkerSettings(),
            new GatewayConfigConverter(converterTemplateService));
        var datalogCacheWriter = new LocalDatalogCacheWriter();
        var parameterTemplateService = new ParameterTemplateServiceImpl(fileSystemSettings, x =>
        {
            if (x == Path.Combine(configPath, "parameter_order.xml"))
                return new MemoryStream(Resources.parameter_order);
            if (x == Path.Combine(configPath, "parameter.xml"))
                return new MemoryStream(Resources.ParameterTemplates);
            throw new NotImplementedException();
        });
        var faultMessageHandler = new FaultMessageHandler();
        var eventPublisher = new LocalDriverImpl();
        var parameterStore = new BetterParameterStore(datalogCacheWriter,
            new LocalSystemMaintenanceStateCacheWriter(),
            converterTemplateService,
            parameterTemplateService,
            faultMessageHandler,
            deviceTemplateService,
            nullLoggerFactory,
            eventPublisher);
        var provider = new ServiceProvider(bundleQueueWorker, parameterStore, unitOfWork);
        ServiceLocator.SetLocatorProvider(() => provider);
        var writerSettings = new BusconfigWriterSettings();
        var busconfigWriter = new BusconfigWriter(converterTemplateService,
            new SchemaViewTemplateServiceImpl(fileSystemSettings, _ => new MemoryStream(Resources.schema_view)),
            new AddressMappingProvider(),
            deviceTemplateService,
            writerSettings,
            parameterTemplateService,
            new DependentDefinitionTemplateServiceImpl(fileSystemSettings, _ => new MemoryStream(Resources.sub_dialogs)),
            new ConditionPoolImpl(),
            new BusconfigInstanceProviderImpl(),
            eventPublisher,
            datalogCacheWriter,
            new GuiTemplateServiceImpl(fileSystemSettings, _ => new MemoryStream(Resources.gui)));
        var responseHandler = new ResponseHandler(busconfigWriter, faultMessageHandler);

        var gc = await nc.DoConnect(ip, IPAddress.Any, 9092, password, streamHandler);
            
        gc.SetupBundleQueueWorker(gw);
        var read = gc.BeginRead(x=>responseHandler.ReadFromGatewayCallback(x));
        var systemconfigRequest = new SystemconfigRequest
        {
            Sid = gc.GatewayID.ToString()
        };
        gc.WriteObject(systemconfigRequest, typeof(SystemconfigRequest), EnXmlInterfaceType.SystemconfigRequest);
        int tries = 0;
        while (tries < 120)
        {
            if (gc.IsOnline)
            {
                var config = new Config
                {
                    Devices = new List<Device>()
                };
                foreach (var device in gc.Devices)
                {
                    config.Devices.Add(new Device
                    {
                        Id = device.Id,
                        ReadBusAddress = device.ReadBusAddress,
                        DeviceTemplateId = device.DeviceTemplateId,
                        Parameters = parameterStore.Parameters
                            .Where(x=>x.Value.DeviceId == device.Id)
                            .Select(x=>new Parameter{ParameterId = x.Value.ParameterId / 100_000}).ToList()
                    });
                }
                await File.WriteAllTextAsync(file, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
                return;
            }
            await Task.Delay(1000);
            tries++;
        }
        Console.Error.WriteLine("Timeout waiting for GW");
    }

    private static string GetEnvString(string name, string defaultValue = default)
    {
        var value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
        if (value is null) return defaultValue;
        return value;
    }
}