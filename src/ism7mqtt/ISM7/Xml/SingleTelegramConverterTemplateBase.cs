using System.Collections.Generic;
using System.Xml.Serialization;
using ism7mqtt.ISM7.Protocol;

namespace ism7mqtt.ISM7.Xml
{
    public abstract class SingleTelegramConverterTemplateBase : ConverterTemplateBase
    {
        [XmlElement("TelegramNr")]
        public ushort TelegramNr { get; set; }

        public override IEnumerable<InfoRead> InfoReads => new[]
        {
            new InfoRead
            {
                InfoNumber = TelegramNr,
                ServiceNumber = ServiceReadNumber ?? -1
            }
        };

        public override void AddTelegram(ushort telegram, byte low, byte high)
        {
            if (telegram == TelegramNr)
                AddTelegram(low, high);
        }

        public override bool CanProcess(ushort telegram)
        {
            return TelegramNr == telegram;
        }

        protected abstract void AddTelegram(byte low, byte high);
    }
}