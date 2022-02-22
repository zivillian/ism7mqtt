using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ism7mqtt.ISM7.Protocol;
using Newtonsoft.Json.Linq;

namespace ism7mqtt.ISM7.Xml
{
    public class UnicodeTextConverter:MultiTelegramConverterTemplateBase
    {
        private bool _broken = false;
        private readonly List<(ushort, int)> _letters = new List<(ushort, int)>();
        public override void AddTelegram(ushort telegram, byte low, byte high)
        {
            if (high != 128 && high != 0)
            {
                _broken = true;
            }
            if (_broken)
            {
                Console.Error.WriteLine($"UnicodeTextConverter: T:{telegram} H:{high} L:{low}");
            }
            if (high == 128 && low == 0) return;
            _letters.Add((telegram, (high << 8) | low));
        }

        public override bool HasValue
        {
            get
            {
                if (_broken)
                    return false;
                return _letters.Count > 0;
            }
        }

        public override JValue GetValue()
        {
            if (_broken)
            {
                throw new NotImplementedException($"CTID '{CTID}' is not yet implemented");
            }
            var sb = new StringBuilder(_letters.Count);
            foreach (var letter in _letters.OrderBy(x=>x.Item1).Select(x=>x.Item2))
            {
                sb.Append((char)letter);
            }
            _letters.Clear();
            return new JValue(sb.ToString());
        }

        public override IEnumerable<InfoWrite> GetWrite(JValue value)
        {
            throw new NotImplementedException($"CTID '{CTID}' is not yet implemented");
        }
    }
}