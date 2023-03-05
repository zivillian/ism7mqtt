using System.Reflection;
using System.Threading.Channels;
using LuCon.SocketServer.GatewayInterface;
using LuCon.SocketServer.SocketServerBase;
using Microsoft.Extensions.Logging;
using Wolf.SocketServer.ISMInterface;

namespace ism7config;

public class BetterBundleQueueWorker:BundleQueueWorker
{
    public BetterBundleQueueWorker(ILoggerFactory loggerFactory, TelegrBundleRequestFactory telegramBundleRequestCreator, BundleQueueWorkerSettings settings, GatewayConfigConverter gatewayConfigConverter) : base(loggerFactory, telegramBundleRequestCreator, settings, gatewayConfigConverter)
    {
    }

    public override async Task RunLoopAsync()
    {
        var type = typeof(BundleQueueWorker);
        var writeFuncInfo = type.GetField("_writeFunc", BindingFlags.Instance | BindingFlags.NonPublic);
        var ctsStreamDisconnectInfo = type.GetField("_ctsStreamDisconnect", BindingFlags.Instance | BindingFlags.NonPublic);
        var chunkReaderInfo = type.GetField("_chunkReader", BindingFlags.Instance | BindingFlags.NonPublic);

        var ctsStreamDisconnect = (CancellationTokenSource)ctsStreamDisconnectInfo.GetValue(this);
        var chunkReader = (ChannelReader<TelegrBundleRequest>)chunkReaderInfo.GetValue(this);
        var writeFunc = (Action<object, Type, EnXmlInterfaceType>)writeFuncInfo.GetValue(this);

        while (!ctsStreamDisconnect.IsCancellationRequested)
        {
            var result = await chunkReader.ReadAsync(ctsStreamDisconnect.Token);
            writeFunc(result, result.GetType(), EnXmlInterfaceType.TgrBundleRequest);
        }

        throw new NotImplementedException();
    }
}