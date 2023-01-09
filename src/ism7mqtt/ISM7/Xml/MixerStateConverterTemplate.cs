using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Xml.Serialization;
using ism7mqtt.ISM7.Protocol;

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

        public override IEnumerable<InfoRead> InfoReads => new[]
        {
            new InfoRead { InfoNumber = TelegramNrOpen, ServiceNumber = ServiceReadNumber ?? -1 },
            new InfoRead { InfoNumber = TelegramNrClose, ServiceNumber = ServiceReadNumber ?? -1 }
        };

        public override bool CanProcess(ushort telegram)
        {
            return TelegramNrOpen == telegram || TelegramNrClose == telegram;
        }

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

        public override bool HasValue => _open.HasValue || _close.HasValue;

        public override JsonValue GetValue()
        {
            JsonValue result;
            if (_open.HasValue && _open.Value != 0)
            {
                result = JsonValue.Create("opened");
            }
            else if (_close.HasValue && _close.Value != 0)
            {
                result = JsonValue.Create("closed");
            }
            else
            {
                result = JsonValue.Create("-");
            }
            _open = null;
            _close = null;
            return result;
        }

        public override IEnumerable<InfoWrite> GetWrite(string value)
        {
            throw new NotImplementedException($"CTID '{CTID}' is not yet implemented");
        }
    }
}