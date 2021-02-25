using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Xml
{
    public class MixerStateConverterTemplate : ConverterTemplateBase
    {
        [XmlElement("TelegramNrOpen")]
        public ushort TelegramNrOpen { get; set; }

        [XmlElement("TelegramNrClose")]
        public ushort TelegramNrClose { get; set; }
    }
}