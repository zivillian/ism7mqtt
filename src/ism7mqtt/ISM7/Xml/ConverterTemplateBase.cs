using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Xml
{
    public abstract class ConverterTemplateBase
    {
        [XmlElement("Type")]
        public string Type { get; set; }
    }
}