using System.Collections.Generic;
using System.Xml.Serialization;
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

        public override bool IsImplemented => true;

        public override bool HasValue => _open.HasValue && _close.HasValue;
        public override JValue GetValue()
        {
            throw new System.NotImplementedException();
        }
    }
}