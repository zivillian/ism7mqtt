using System;
using System.Collections.Generic;
using ism7mqtt.ISM7.Protocol;
using Newtonsoft.Json.Linq;

namespace ism7mqtt.ISM7.Xml
{
    public class BM2DateConverterTemplate:SingleTelegramConverterTemplateBase
    {
        private DateTime? _value;

        protected override void AddTelegram(byte low, byte high)
        {
            var value = high << 8 | low;
            var day = value & 0b1_1111;
            var month = (value >> 5) & 0xf;
            var year = value >> 9;
            _value = new DateTime(2000 + year, month + 1, day + 1);
        }

        public override bool HasValue => _value.HasValue;

        public override JValue GetValue()
        {
            var result = new JValue(_value.Value);
            _value = null;
            return result;
        }

        public override IEnumerable<InfoWrite> GetWrite(JValue value)
        {
            throw new NotImplementedException($"CTID '{CTID}' is not yet implemented");
        }
    }
}