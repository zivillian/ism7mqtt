using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using ism7mqtt.ISM7.Protocol;

namespace ism7mqtt.ISM7.Xml
{
    public class Bit4to7ConverterTemplate : SingleTelegramConverterTemplateBase
    {
        private byte? _value;

        protected override void AddTelegram(byte low, byte high)
        {
            if (high != 0)
                throw new NotImplementedException("high byte does not make any sense");
            _value = (byte) (low >> 4);
        }

        public override bool HasValue => _value.HasValue;

        public override JsonValue GetValue()
        {
            if (!HasValue)
                throw new InvalidOperationException();
            var result = JsonValue.Create(_value.Value);
            _value = null;
            return result;
        }

        public override IEnumerable<InfoWrite> GetWrite(JsonValue value)
        {
            throw new NotImplementedException($"CTID '{CTID}' is not yet implemented");
        }
    }
}