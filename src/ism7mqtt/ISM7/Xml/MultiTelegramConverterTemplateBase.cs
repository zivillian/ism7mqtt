using ism7mqtt.ISM7.Protocol;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Xml
{
    public abstract class MultiTelegramConverterTemplateBase : ConverterTemplateBase
    {
        [XmlArray("TelegramNumbers")]
        [XmlArrayItem("unsignedShort")]
        public List<ushort> TelegramNumbers { get; set; }

        public override IEnumerable<InfoRead> InfoReads => TelegramNumbers.Select(x =>
            new InfoRead
            {
                InfoNumber = x,
                ServiceNumber = ServiceReadNumber ?? -1
            });

        public override bool CanProcess(ushort telegram)
        {
            return TelegramNumbers.Any(x => x == telegram);
        }
    }
}