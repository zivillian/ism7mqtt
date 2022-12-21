using System;
using System.Collections.Generic;
using System.Globalization;
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
                    if (!UInt16.TryParse(value.ToString(), out data)) yield break;
                    break;
                case "SS":
                    if (!Int16.TryParse(value.ToString(), out var int16)) yield break;
                    data = (ushort) int16;
                    break;
                case "SS10":
                    if (!Double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) yield break;
                    data = (ushort) (parsed * 10);
                    break;
                case "US10":
                    if (!Double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed)) yield break;
                    data = (ushort) (parsed * 10);
                    break;
                case "SS100":
                    if (!Double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed)) yield break;
                    data = (ushort) (parsed * 100);
                    break;
                case "SSPR":
                    if (!Double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed)) yield break;
                    data = (ushort) (parsed * (1.0/256));
                    break;
                case "US4":
                    if (!Double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed)) yield break;
                    data = (ushort) (parsed * 4);
                    break;
                case "IntDiv60":
                    if (!Double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed)) yield break;
                    data = (ushort) (parsed * 60);
                    break;
                default:
                    throw new NotImplementedException($"type '{Type}' for CTID '{CTID}' is not yet implemented");
            }
            yield return new InfoWrite{InfoNumber = TelegramNr, DBLow = $"0x{(data & 0xff):X2}", DBHigh = $"0x{(data >> 8):X2}"};
        }
    }
}