using System;
using System.Collections.Generic;
using System.Linq;
using ism7mqtt.ISM7.Protocol;
using Newtonsoft.Json.Linq;

namespace ism7mqtt.ISM7.Xml
{
    public class SolarStatisticConverterTemplate : MultiTelegramConverterTemplateBase
    {
        private const string KwType = "SolarertragW_kW";
        private const string KwMwType = "SolarertragW_kW_MW";
        private readonly SortedDictionary<ushort, uint> _values = new();
        private bool _hasValue = false;

        public override void AddTelegram(ushort telegram, byte low, byte high)
        {
            if (Type == KwMwType || Type == KwType)
            {
                var value = (uint)(high << 8) | low;
                if (!_values.ContainsKey(telegram))
                {
                    _values.Add(telegram, value);
                }
                else
                {
                    _values[telegram] = value;
                }
                _hasValue = true;
            }
            else
            {
                Console.Error.WriteLine($"SolarStatisticConverter({Type}): T:{telegram} H:{high} L:{low}");
            }
        }

        public override bool HasValue => _hasValue;

        public override JValue GetValue()
        {
            var values = _values.Values.ToList();
            var wh = values[0];
            ulong value = wh;
            if (values.Count > 1)
            {
                var kwh = values[1];
                value += kwh * 1_000UL;
            }
            if (values.Count > 2)
            {
                var mwh = values[2];
                value += mwh * 1_000_000UL;
            }
            _hasValue = false;
            return new JValue(value);
        }

        public override IEnumerable<InfoWrite> GetWrite(JValue value)
        {
            throw new NotImplementedException($"CTID '{CTID}' is not yet implemented");
        }
    }
}