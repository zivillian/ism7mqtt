using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using ism7mqtt.ISM7.Protocol;
using Newtonsoft.Json.Linq;

namespace ism7mqtt.ISM7.Xml
{
    public class MixerStateConverterTemplate : ConverterTemplateBase
    {
        [XmlElement("TelegramNrOpen")]
        public ushort TelegramNrOpen { get; set; }

        private ushort? _open;

        [XmlElement("TelegramNrClose")]
        public ushort TelegramNrClose { get; set; }

        private ushort? _close;
        
        public override IEnumerable<ushort> TelegramIds => new[] {TelegramNrOpen, TelegramNrClose};
        
        public override void AddTelegram(ushort telegram, byte low, byte high)
        {
            if (telegram == TelegramNrOpen)
            {
                _open = (ushort) ((high << 8) | low);
            }
            else if (telegram == TelegramNrClose)
            {
                _close = (ushort) ((high << 8) | low);
            }
        }

        public override bool HasValue => _open.HasValue && _close.HasValue;

        public override JValue GetValue()
        {
            JValue result;
            if (_open.HasValue && _open.Value != 0)
            {
                result = new JValue("opened");
            }
            else if (_close.HasValue && _close.Value != 0)
            {
                result = new JValue("closed");
            }
            else
            {
                result = new JValue("-");
            }
            _open = null;
            _close = null;
            return result;
        }

        public override IEnumerable<InfoWrite> GetWrite(JValue value)
        {
            throw new NotImplementedException($"CTID '{CTID}' is not yet implemented");
        }
    }
}