using System.Collections.Generic;
using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Xml
{
    public abstract class SingleTelegramConverterTemplateBase : ConverterTemplateBase
    {
        [XmlElement("TelegramNr")]
        public ushort TelegramNr { get; set; }

        public override IEnumerable<ushort> TelegramIds => new[] {TelegramNr};

        public override void AddTelegram(ushort telegram, byte low, byte high)
        {
            if (telegram == TelegramNr)
                AddTelegram(low, high);
        }

        protected abstract void AddTelegram(byte low, byte high);
    }
}