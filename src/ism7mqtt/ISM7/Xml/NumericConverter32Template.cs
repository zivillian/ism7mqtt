using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Xml
{
    public class NumericConverter32Template:ConverterTemplateBase
    {
        [XmlElement("TelegramNrHigh")]
        public ushort TelegramNrHigh { get; set; }

        [XmlElement("TelegramNrLow")]
        public ushort TelegramNrLow { get; set; }
    }
}