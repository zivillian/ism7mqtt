using System;
using System.Collections.Generic;
using ism7mqtt.ISM7.Protocol;
using Newtonsoft.Json.Linq;

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

        public override JValue GetValue()
        {
            if (!HasValue)
                throw new InvalidOperationException();
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