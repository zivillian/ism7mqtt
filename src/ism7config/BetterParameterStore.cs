using System.Collections.Concurrent;
using System.Reflection;
using LuCon.Common.ConfigService;
using LuCon.SocketServer.SocketServerBase;
using Microsoft.Extensions.Logging;
using Wolf.DRIVR.Model;
using Wolf.SocketServer.ISMInterface;

namespace ism7config;

public class BetterParameterStore : ParameterStore
{
    private readonly FieldInfo _parametersByIdInfo;

    public BetterParameterStore(IDatalogCacheWriter datalogCacheWriter, ISystemMaintenanceStateCacheWriter systemMaintenanceStateCacheWriter, IConverterTemplateService converterTemplateService, IParameterTemplateService parameterTemplateService, IFaultMessageResponseBatchHandler faultMessageResponseBatchHandler, IDeviceTemplateService deviceTemplateService, ILoggerFactory loggerFactory, IDRIVR eventPublisher) : base(datalogCacheWriter, systemMaintenanceStateCacheWriter, converterTemplateService, parameterTemplateService, faultMessageResponseBatchHandler, deviceTemplateService, loggerFactory, eventPublisher)
    {
        _parametersByIdInfo = typeof(ParameterStore).GetField("_parametersById", BindingFlags.Instance | BindingFlags.NonPublic);
    }

    public ConcurrentDictionary<long, LuCon.SocketServer.SocketServerBase.Parameter> Parameters => (ConcurrentDictionary<long, LuCon.SocketServer.SocketServerBase.Parameter>)_parametersByIdInfo.GetValue(this);
}