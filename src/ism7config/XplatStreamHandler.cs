using System.Net.Sockets;
using ism7ssl;
using LuCon.WebPortal.StandaloneService;

namespace ism7config;

public class XplatStreamHandler : IStreamHandler
{
    public async Task<Stream> GetStream(TcpClient tcpClient, int port)
    {
        try
        {
            var ssl = new Ism7SslStream(tcpClient.Client);
            using var cts = new CancellationTokenSource(60000);
            await ssl.AuthenticateAsClientAsync(cts.Token);
            return ssl;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            throw;
        }
    }
}