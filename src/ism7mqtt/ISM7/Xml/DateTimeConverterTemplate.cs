using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using ism7mqtt.ISM7.Protocol;

namespace ism7mqtt.ISM7.Xml
{
    public class DateTimeConverterTemplate : MultiTelegramConverterTemplateBase
    {
        private ushort?[] _values;

        public override void AddTelegram(ushort telegram, byte low, byte high)
        {
            try
            {
                _values ??= new ushort?[TelegramNumbers.Count];
                var index = TelegramNumbers.IndexOf(telegram);
                if (index < 0)
                    return;
                var value = (ushort)((high << 8) | low);
                if (!IsValid(index, value))
                {
                    Console.Error.WriteLine($"received invalid value {value} for {nameof(DateTimeConverterTemplate)}({telegram}). Make sure you have a correct date/time set on your device");
                    return;
                }
                _values[index] = value;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"failed to process telegram {telegram} for {nameof(DateTimeConverterTemplate)}({CTID}): {ex}");
            }
        }

        private bool IsValid(int index, ushort value)
        {
            switch (Type)
            {
                case "TIME_hh_mm_ss":
                    return index switch
                    {
                        0 => value <= 23,
                        1 => value <= 59,
                        2 => value <= 59,
                        _ => false
                    };
                case "Date_DD_MM_YY":
                    return index switch
                    {
                        0 => value is >= 1 and <= 31,
                        1 => value is >= 1 and <= 12,
                        2 => value <= 99,
                        _ => false
                    };
                default:
                    return true;
            }
        }

        public override bool HasValue
        {
            get
            {
                try
                {
                    return _values != null && Array.TrueForAll(_values, x => x.HasValue);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"failed to evaluate {nameof(DateTimeConverterTemplate)}({CTID}).{nameof(HasValue)}: {ex}");
                    return false;
                }
            }
        }

        public override JsonValue GetValue()
        {
            try
            {
                if (!HasValue)
                    throw new InvalidOperationException();
                JsonValue result;
                switch (Type)
                {
                    case "TIME_hh_mm_ss":
                        result = JsonValue.Create(new TimeSpan(_values[0]!.Value, _values[1]!.Value, _values[2]!.Value).ToString());
                        break;
                    case "Date_DD_MM_YY":
                        result = JsonValue.Create(new DateTime(2000 + _values[2]!.Value, _values[1]!.Value, _values[0]!.Value).ToString());
                        break;
                    default:
                        throw new NotImplementedException($"type '{Type}' for CTID '{CTID}' is not yet implemented");
                }
                Array.Clear(_values, 0, _values.Length);
                return result;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"failed to get value for {nameof(DateTimeConverterTemplate)}({CTID}): {ex}");
                if (_values != null)
                    Array.Clear(_values, 0, _values.Length);
                return null;
            }
        }

        public override IEnumerable<InfoWrite> GetWrite(string value)
        {
            throw new NotImplementedException($"CTID '{CTID}' is not yet implemented");
        }

        public override ConverterTemplateBase Clone()
        {
            return Clone(new DateTimeConverterTemplate());
        }
    }
}
