using System;
using System.Collections.Generic;
using ism7mqtt.ISM7.Protocol;
using Newtonsoft.Json.Linq;

namespace ism7mqtt.ISM7.Xml
{
    public class UnicodeTextConverter:MultiTelegramConverterTemplateBase
    {
        public override void AddTelegram(ushort telegram, byte low, byte high)
        {
            Console.Error.WriteLine($"UnicodeTextConverter: T:{telegram} H:{high} L:{low}");
        }

        public override bool HasValue => false;

        public override JValue GetValue()
        {
            throw new NotImplementedException($"CTID '{CTID}' is not yet implemented");
        }

        public override IEnumerable<InfoWrite> GetWrite(JValue value)
        {
            throw new NotImplementedException($"CTID '{CTID}' is not yet implemented");
        }
    }
}