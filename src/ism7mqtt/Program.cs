using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace ism7mqtt
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var client = new Ism7Client();
            await client.RunAsync(IPAddress.Parse(args[0]), args[1], CancellationToken.None);
        }
    }
}
