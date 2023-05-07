using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using LuCon.WebPortal.StandaloneService;

namespace ism7config;

public class XplatStreamHandler : IStreamHandler
{
    private readonly MethodInfo _loadCertMethodInfo;

    public XplatStreamHandler()
    {
        _loadCertMethodInfo = typeof(SslStreamHandler).GetMethod("LoadClientCertificate", BindingFlags.Instance | BindingFlags.NonPublic);
    }
    public async Task<Stream> GetStream(TcpClient tcpClient, int port)
    {
        try
        {
            var certificate = (X509Certificate)_loadCertMethodInfo.Invoke(new SslStreamHandler(), null);
            var ssl = new SslStream(tcpClient.GetStream(), false, (a, b, c, d) => true);

            var sslOptions = new SslClientAuthenticationOptions
            {
                TargetHost = "ism7.server",
                ClientCertificates = new X509Certificate2Collection(new X509Certificate2(certificate)),
            };
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    sslOptions.CipherSuitesPolicy = new CipherSuitesPolicy(new[]
                    {
                        TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA256
                    });
                }
                catch (PlatformNotSupportedException)
                {
                    //older linux or mac https://github.com/dotnet/runtime/issues/33649
                }
            }
            using var cts = new CancellationTokenSource(60000);
            await ssl.AuthenticateAsClientAsync(sslOptions, cts.Token);
            return ssl;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            throw;
        }
    }
}