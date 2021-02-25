using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Xml
{
    public class NullConverterTemplate : ConverterTemplateBase
    {
        [XmlElement("XMLName")]
        public string XMLName { get; set; }

        [XmlElement("IntervalSec")]
        public int IntervalSec { get; set; }

    }
}