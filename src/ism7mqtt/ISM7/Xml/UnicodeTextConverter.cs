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
            throw new NotImplementedException();
        }

        public override bool HasValue => throw new NotImplementedException();

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