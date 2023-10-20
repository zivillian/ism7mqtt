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
            if (!HasValue)
                throw new InvalidOperationException();
            var result = JsonValue.Create(_value.Value.ToString());
            _value = null;
            return result;
        }

        public override IEnumerable<InfoWrite> GetWrite(string value)
        {
            var date = DateOnly.Parse(value);
            var bytes = date.Day - 1;
            bytes |= (date.Month - 1) << 5;
            bytes |= (date.Year - 2000) << 9;
            var low = (byte)(bytes & 0xff);
            var high = (byte)(bytes >> 8);
            yield return new InfoWrite{InfoNumber = TelegramNr, DBLow = $"0x{low:X2}", DBHigh = $"0x{high:X2}"};
        }

        public override ConverterTemplateBase Clone()
        {
            return Clone(new BM2DateConverterTemplate());
        }
    }
}