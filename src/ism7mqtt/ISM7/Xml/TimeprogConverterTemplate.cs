using Newtonsoft.Json.Linq;

namespace ism7mqtt.ISM7.Xml
{
    public class TimeprogConverterTemplate:MultiTelegramConverterTemplateBase
    {
        public override void AddTelegram(ushort telegram, byte low, byte high)
        {
        }

        public override bool IsImplemented => false;

        public override bool HasValue => false;
        public override JValue GetValue()
        {
            throw new System.NotImplementedException();
        }
    }
}