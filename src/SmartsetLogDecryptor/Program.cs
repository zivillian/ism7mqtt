using Mono.Options;
using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SmartsetLogDecryptor
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var path = @"smartset.log";
            var showHelp = false;
            var options = new OptionSet
            {
                {"f|file=", $"Path to smartset.log - defaults to {path}", x => path = x},
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
                Console.Error.Write("SmartsetLogDecryptor: ");
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine("Try 'SmartsetLogDecryptor --help' for more information");
                return;
            }
            if (!File.Exists(path)) showHelp = true;
            if (showHelp)
            {
                options.WriteOptionDescriptions(Console.Out);
                return;
            }

            var key = new byte[] { 206, 68, 65, 1, 130, 204, 240, 165, 51, 138, 88, 29, 131, 126, 245, 140, 151, 37, 217, 135, 217, 166, 238, 230, 31, 211, 109, 116, 126, 93, 67, 224 };
            var iv = new byte[] { 30, 46, 172, 209, 80, 46, 223, 99, 178, 48, 78, 183, 252, 161, 12, 213 };

            using (var aes = Rijndael.Create())
            {
                aes.BlockSize = 128;
                aes.KeySize = 128;
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (ICryptoTransform cryptoTransform = aes.CreateDecryptor(aes.Key, aes.IV))
                {
                    using (var logfile = File.OpenRead(path))
                    {
                        var lbuffer = new byte[4];
                        while (logfile.CanRead && logfile.Position < logfile.Length)
                        {
                            logfile.Read(lbuffer);
                            if (BitConverter.IsLittleEndian)
                            {
                                Array.Reverse(lbuffer);
                            }
                            var length = BitConverter.ToInt32(lbuffer);
                            if (length < 0)
                            {
                                Array.Reverse(lbuffer);
                                length = BitConverter.ToInt32(lbuffer);
                            }
                            var buffer = ArrayPool<byte>.Shared.Rent(length);
                            logfile.Read(buffer, 0, length);
                            var plain = cryptoTransform.TransformFinalBlock(buffer, 0, length);
                            Console.Write(Encoding.UTF8.GetString(plain));
                            ArrayPool<byte>.Shared.Return(buffer);
                        }
                    }
                }
            }
        }
    }
}