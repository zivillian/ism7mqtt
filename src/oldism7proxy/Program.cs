using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Mono.Options;
using Org.BouncyCastle.Tls;

namespace oldism7proxy
{
    internal class Program
    {
        private static bool _debug = false;

        static async Task Main(string[] args)
        {
            var showHelp = false;
            string host = null;
            var options = new OptionSet
            {
                {"i|ipAddress=", "Wolf Hostname or IP address", x => host = x},
                {"d|debug", "dump raw xml messages", x => _debug = x != null},
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
                Console.Error.Write("oldism7proxy: ");
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine("Try 'oldism7proxy --help' for more information");
                return;
            }
            if (showHelp || host is null)
            {
                options.WriteOptionDescriptions(Console.Out);
                return;
            }
            try
            {
                var server = new TcpListener(IPAddress.Any, 9092);
                using (var cts = new CancellationTokenSource())
                {
                    Console.CancelKeyPress += (s, e) =>
                    {
                        e.Cancel = true;
                        cts.Cancel();
                    };
                    server.Start();
                    while (!cts.IsCancellationRequested)
                    {
                        using(var incoming = await server.AcceptTcpClientAsync(cts.Token))
                        using (var outgoing = new TcpClient())
                        using(var sslServer = new SslStream(incoming.GetStream(), true, (_, _, _, _) => true))
                        {
                            var tlsClient = new Ism7TlsClient();
                            await outgoing.ConnectAsync(host, 9091, cts.Token);
                            var protocol = new TlsClientProtocol(outgoing.GetStream());
                            protocol.Connect(tlsClient);
                            var sslClient = protocol.Stream;

                            var certificate = new X509Certificate2(Resources.client);

                            var sslServerOptions = new SslServerAuthenticationOptions
                            {
                                ServerCertificate = certificate,
                                ClientCertificateRequired = true
                            };
                            
                            if (!OperatingSystem.IsWindows())
                            {
                                try
                                {
                                    sslServerOptions.CipherSuitesPolicy = new CipherSuitesPolicy(new[]
                                    {
                                        TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA256
                                    });
                                }
                                catch (PlatformNotSupportedException)
                                {
                                    //older linux or mac https://github.com/dotnet/runtime/issues/33649
                                }
                            }
                            await sslServer.AuthenticateAsServerAsync(sslServerOptions, cts.Token);
                            
                            var sslOptions = new SslClientAuthenticationOptions
                            {
                                TargetHost = "ism7.server",
                                ClientCertificates = new X509Certificate2Collection(certificate),
                            };
                            if (!OperatingSystem.IsWindows())
                            {
                                sslOptions.CipherSuitesPolicy = new CipherSuitesPolicy(new[]
                                {
                                    TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA256
                                });
                            }
                            
                            var toServer = CopyAsync("< ", sslClient, sslServer, cts.Token);
                            var toClient = CopyAsync("> ", sslServer, sslClient, cts.Token);
                            await Task.WhenAny(toServer, toClient);
                        }
                    }
                }
            }
            catch(OperationCanceledException)
            {}
        }

        private static Task CopyAsync(string prefix, Stream source, Stream target, CancellationToken cancellationToken)
        {
            return Task.Run(() => Copy(prefix, source, target, cancellationToken), cancellationToken);
        }

        private static void Copy(string prefix, Stream source, Stream target, CancellationToken cancellationToken)
        {
            var buf = new byte[4096];
            while (!cancellationToken.IsCancellationRequested)
            {
                var count = source.Read(buf);
                if (count == 0) return;
                if (_debug)
                {
                    var start = buf.AsSpan(0, count).IndexOf((byte)0x3c);
                    if (start < 0)
                        Console.WriteLine($"{prefix}({HexEncode(buf.AsSpan(0, count))})");
                    else if (start == 0)
                        Console.WriteLine(prefix + Encoding.UTF8.GetString(buf.AsSpan(0, count)));
                    else
                        Console.WriteLine($"{prefix}({HexEncode(buf.AsSpan(0, start))}):{Encoding.UTF8.GetString(buf.AsSpan(start, count - start))}");
                }
                target.Write(buf.AsSpan(0, count));
            }
        }

        private static string HexEncode(ReadOnlySpan<byte> data)
        {
            StringBuilder s = new StringBuilder(data.Length*2);
            foreach (byte b in data)
                s.Append(b.ToString("x2"));
            return s.ToString();
        }
    }
}