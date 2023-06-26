using Wolf.SocketServer.ISMInterface;

namespace ism7config;

public class FaultMessageHandler : IFaultMessageResponseBatchHandler
{
    public void Start()
    {
        throw new NotImplementedException();
    }

    public void Stop()
    {
        throw new NotImplementedException();
    }

    public void AddResponse(TelegrBundleResponse resp)
    {
    }

    public void ScheduleRuntimeAlertDataUpdate(long gatewayId, long deviceId, TimeSpan timespan,
        ModbusDeviceRuntimeAlertData modbusDeviceRuntimeAlertData)
    {
        throw new NotImplementedException();
    }
}