using System;
using System.ComponentModel;

namespace ism7mqtt
{
    public static class Converter
    {
        public static byte FromHex(string hex)
        {
            if (hex.Length > 4 || !hex.StartsWith("0x"))
                throw new ArgumentException($"'{hex}' is not a hex value", nameof(hex));
            return Convert.ToByte(hex.Substring(2), 16);
        }
    }
}