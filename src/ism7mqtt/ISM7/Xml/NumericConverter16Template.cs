using System;
using System.Collections.Generic;
using ism7mqtt.ISM7.Protocol;
using Newtonsoft.Json.Linq;

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

        public override JValue GetValue()
        {
            if (!HasValue)
                throw new InvalidOperationException();
            JValue result;
            switch (Type)
            {
                case "US":
                    result = new JValue(_value.Value);
                    _value = null;
                    return result;
                case "SS":
                    result = new JValue((short)_value.Value);
                    _value = null;
                    return result;
                case "SS10":
                    result = new JValue(((short)_value.Value) / 10.0);
                    _value = null;
                    return result;
                case "US10":
                    result = new JValue(_value.Value / 10.0);
                    _value = null;
                    return result;
                case "SS100":
                    result = new JValue(((short)_value.Value) / 100.0);
                    _value = null;
                    return result;
                case "SSPR":
                    result = new JValue(((short) _value.Value) * (1.0 / 255));
                    _value = null;
                    return result;
                case "US4":
                    result = new JValue(_value.Value / 4.0);
                    _value = null;
                    return result;
                case "IntDiv60":
                    result = new JValue(_value.Value / 60.0);
                    _value = null;
                    return result;
                default:
                    throw new NotImplementedException($"type '{Type}' for CTID '{CTID}' is not yet implemented");
            }
        }

        public override IEnumerable<InfoWrite> GetWrite(JValue value)
        {
            ushort data;
            switch (Type)
            {
                case "US":
                    data = UInt16.Parse(value.Value.ToString());
                    break;
                case "SS":
                    data = (ushort) Int16.Parse(value.Value.ToString());
                    break;
                case "SS10":
                    data = (ushort) (Double.Parse(value.Value.ToString()) * 10);
                    break;
                case "US10":
                    data = (ushort) (Double.Parse(value.Value.ToString()) * 10);
                    break;
                case "SS100":
                    data = (ushort) (Double.Parse(value.Value.ToString()) * 100);
                    break;
                case "SSPR":
                    data = (ushort) (Double.Parse(value.Value.ToString()) * (1.0/256));
                    break;
                case "US4":
                    data = (ushort) (Double.Parse(value.Value.ToString()) * 4);
                    break;
                case "IntDiv60":
                    data = (ushort) (Double.Parse(value.Value.ToString()) * 60);
                    break;
                default:
                    throw new NotImplementedException($"type '{Type}' for CTID '{CTID}' is not yet implemented");
            }
            yield return new InfoWrite{InfoNumber = TelegramNr, DBLow = $"0x{(data & 0xff):X2}", DBHigh = $"0x{(data >> 8):X2}"};
        }
    }
}