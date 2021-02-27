using System;
using Newtonsoft.Json.Linq;

namespace ism7mqtt.ISM7.Xml
{
    public class UnicodeTextConverter:MultiTelegramConverterTemplateBase
    {
        public override void AddTelegram(ushort telegram, byte low, byte high)
        {
            throw new NotImplementedException();
        }

        public override bool IsImplemented => true;

        public override bool HasValue => throw new NotImplementedException();
        public override JValue GetValue()
        {
            throw new NotImplementedException();
        }
    }
}