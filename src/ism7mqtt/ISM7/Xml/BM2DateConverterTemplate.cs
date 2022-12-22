using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using ism7mqtt.ISM7.Protocol;

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

        public override JsonValue GetValue()
        {
            var result = JsonValue.Create(_value.Value.ToString());
            _value = null;
            return result;
        }

        public override IEnumerable<InfoWrite> GetWrite(JsonValue value)
        {
            throw new NotImplementedException($"CTID '{CTID}' is not yet implemented");
        }
    }
}