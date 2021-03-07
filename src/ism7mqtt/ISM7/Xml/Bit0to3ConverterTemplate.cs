using System;
using Newtonsoft.Json.Linq;

namespace ism7mqtt.ISM7.Xml
{
    public class Bit0to3ConverterTemplate : SingleTelegramConverterTemplateBase
    {
        private byte? _value;

        protected override void AddTelegram(byte low, byte high)
        {
            if (high != 0)
                throw new NotImplementedException("high byte does not make any sense");
            _value = (byte) (low & 0xf);
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
    }
}