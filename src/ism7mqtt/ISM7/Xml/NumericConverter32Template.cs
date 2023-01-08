using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Xml.Serialization;
using ism7mqtt.ISM7.Protocol;

namespace ism7mqtt.ISM7.Xml
{
    public class NumericConverter32Template:ConverterTemplateBase
    {
        [XmlElement("TelegramNrHigh")]
        public ushort TelegramNrHigh { get; set; }

        private ushort? _high;

        [XmlElement("TelegramNrLow")]
        public ushort TelegramNrLow { get; set; }

        private ushort? _low;

        public override IEnumerable<InfoRead> InfoReads => new[]
        {
            new InfoRead { InfoNumber = TelegramNrHigh, ServiceNumber = ServiceReadNumber ?? -1 },
            new InfoRead { InfoNumber = TelegramNrLow, ServiceNumber = ServiceReadNumber ?? -1 }
        };

        public override bool CanProcess(ushort telegram)
        {
            return TelegramNrHigh == telegram || TelegramNrLow == telegram;
        }

        public override void AddTelegram(ushort telegram, byte low, byte high)
        {
            if (telegram == TelegramNrLow)
            {
                _low = (ushort) ((high << 8) | low);
            }
            else if (telegram == TelegramNrHigh)
            {
                _high = (ushort) ((high << 8) | low);
            }
        }

        public override bool HasValue => _high.HasValue && _low.HasValue;

        public override JsonValue GetValue()
        {
            if (!HasValue)
                throw new InvalidOperationException();
            var value = (_high.Value << 16) | _low.Value;
            JsonValue result;
            switch (Type)
            {
                case "UINT32":
                    result = JsonValue.Create((uint) value);
                    _high = null;
                    _low = null;
                    return result;
                default:
                    throw new NotImplementedException($"type '{Type}' for CTID '{CTID}' is not yet implemented");
            }
        }

        public override IEnumerable<InfoWrite> GetWrite(JsonValue value)
        {
            ushort low;
            ushort high;
            switch (Type)
            {
                case "UINT32":
                    if (!UInt32.TryParse(value.ToString(), out var data)) yield break;
                    low = (ushort) (data & 0xffff);
                    high = (ushort) (data >> 16);
                    break;
                default:
                    throw new NotImplementedException($"type '{Type}' for CTID '{CTID}' is not yet implemented");
            }
            yield return new InfoWrite{InfoNumber = TelegramNrLow, DBLow = $"0x{(low & 0xff):X2}", DBHigh = $"0x{(low >> 8):X2}"};
            yield return new InfoWrite{InfoNumber = TelegramNrHigh, DBLow = $"0x{(high & 0xff):X2}", DBHigh = $"0x{(high >> 8):X2}"};
        }
    }
}