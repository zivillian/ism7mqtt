using System;
using Newtonsoft.Json.Linq;

namespace ism7mqtt.ISM7.Xml
{
    public class BM2TimeConverterTemplate:SingleTelegramConverterTemplateBase
    {
        private TimeSpan? _value;

        protected override void AddTelegram(byte low, byte high)
        {
            _value = TimeSpan.FromHours(high).Add(TimeSpan.FromMinutes(low));
        }

        public override bool HasValue => _value.HasValue;

        public override JValue GetValue()
        {
            var result = new JValue(_value.Value);
            _value = null;
            return result;
        }
    }
}