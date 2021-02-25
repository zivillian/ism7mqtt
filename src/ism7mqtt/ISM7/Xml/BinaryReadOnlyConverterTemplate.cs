using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Xml
{
    public class BinaryReadOnlyConverterTemplate : SingleTelegramConverterTemplateBase
    {
        [XmlElement("OnValue")]
        public int OnValue { get; set; }

        [XmlElement("Bitnumber")]
        public int Bitnumber { get; set; }

    }
}