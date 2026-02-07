using System.Collections.Generic;
using System.Xml.Serialization;
using ism7mqtt.ISM7.Protocol;

namespace ism7mqtt.ISM7.Xml
{
    public abstract class SingleTelegramConverterTemplateBase : ConverterTemplateBase
    {
        [XmlElement("TelegramNr")]
        public ushort? TelegramNr { get; set; }

        public override int InfoReadCount => 1;

        public override IEnumerable<InfoRead> InfoReads
        {
            get
            {
                if (!TelegramNr.HasValue) yield break;
                yield return new InfoRead
                {
                    InfoNumber = TelegramNr.Value,
                    ServiceNumber = ServiceReadNumber ?? -1
                };
            }
        }

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

        protected override ConverterTemplateBase Clone(ConverterTemplateBase clone)
        {
            if (clone is SingleTelegramConverterTemplateBase single)
            {
                single.TelegramNr = TelegramNr;
            }
            return base.Clone(clone);
        }
    }
}