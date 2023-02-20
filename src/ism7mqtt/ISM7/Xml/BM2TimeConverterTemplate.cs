using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using ism7mqtt.ISM7.Protocol;

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
            var time = TimeSpan.Parse(value);
            var high = time.Hours;
            var low = time.Minutes;
            yield return new InfoWrite{InfoNumber = TelegramNr, DBLow = $"0x{low:X2}", DBHigh = $"0x{high:X2}"};
        }
    }
}