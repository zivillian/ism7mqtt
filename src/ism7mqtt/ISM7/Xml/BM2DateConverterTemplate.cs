using System;
using Newtonsoft.Json.Linq;

namespace ism7mqtt.ISM7.Xml
{
    public class BM2DateConverterTemplate:SingleTelegramConverterTemplateBase
    {
        protected override void AddTelegram(byte low, byte high)
        {

        }

        public override bool IsImplemented => false;

        public override bool HasValue => false;
        public override JValue GetValue()
        {
            throw new NotImplementedException();
        }
    }
}