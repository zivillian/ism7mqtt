using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using ism7mqtt.ISM7.Protocol;

namespace ism7mqtt.ISM7.Xml
{
    public class NumericConverter16Template:SingleTelegramConverterTemplateBase
    {
        private ushort? _value;

        protected override void AddTelegram(byte low, byte high)
        {
            _value = (ushort) ((high << 8) | low);
        }

        public override bool HasValue => _value.HasValue;

        public override JsonValue GetValue()
        {
            if (!HasValue)
                throw new InvalidOperationException();
            JsonValue result;
            switch (Type)
            {
                case "US":
                    result = JsonValue.Create(_value.Value);
                    _value = null;
                    return result;
                case "SS":
                    result = JsonValue.Create((short)_value.Value);
                    _value = null;
                    return result;
                case "SS10":
                    result = JsonValue.Create(((short)_value.Value) / 10.0);
                    _value = null;
                    return result;
                case "US10":
                    result = JsonValue.Create(_value.Value / 10.0);
                    _value = null;
                    return result;
                case "SS100":
                    result = JsonValue.Create(((short)_value.Value) / 100.0);
                    _value = null;
                    return result;
                case "SSPR":
                    result = JsonValue.Create(((short) _value.Value) * (1.0 / 255));
                    _value = null;
                    return result;
                case "US4":
                    result = JsonValue.Create(_value.Value / 4.0);
                    _value = null;
                    return result;
                case "IntDiv60":
                    result = JsonValue.Create(_value.Value / 60.0);
                    _value = null;
                    return result;
                default:
                    throw new NotImplementedException($"type '{Type}' for CTID '{CTID}' is not yet implemented");
            }
        }

        public override IEnumerable<InfoWrite> GetWrite(JsonValue value)
        {
            ushort data;
            switch (Type)
            {
                case "US":
                    data = value.GetValue<UInt16>();
                    break;
                case "SS":
                    data = (ushort) value.GetValue<short>();
                    break;
                case "SS10":
                    data = (ushort) (value.GetValue<double>() * 10);
                    break;
                case "US10":
                    data = (ushort) (value.GetValue<double>() * 10);
                    break;
                case "SS100":
                    data = (ushort) (value.GetValue<double>() * 100);
                    break;
                case "SSPR":
                    data = (ushort) (value.GetValue<double>() * (1.0/256));
                    break;
                case "US4":
                    data = (ushort) (value.GetValue<double>() * 4);
                    break;
                case "IntDiv60":
                    data = (ushort) (value.GetValue<double>() * 60);
                    break;
                default:
                    throw new NotImplementedException($"type '{Type}' for CTID '{CTID}' is not yet implemented");
            }
            yield return new InfoWrite{InfoNumber = TelegramNr, DBLow = $"0x{(data & 0xff):X2}", DBHigh = $"0x{(data >> 8):X2}"};
        }
    }
}