using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Xml
{
    public abstract class SingleTelegramConverterTemplateBase : ConverterTemplateBase
    {
        [XmlElement("TelegramNr")]
        public ushort TelegramNr { get; set; }
    }
}