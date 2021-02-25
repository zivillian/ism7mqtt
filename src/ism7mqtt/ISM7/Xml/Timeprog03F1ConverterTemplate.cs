using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Xml
{
    public class Timeprog03F1ConverterTemplate:ConverterTemplateBase
    {
        [XmlElement("DayOfWeek")]
        public byte DayOfWeek { get; set; }

        [XmlElement("HeatprogNumber")]
        public int HeatprogNumber { get; set; }

        [XmlElement("ProgramType")]
        public int ProgramType { get; set; }

        [XmlElement("HzkInstance")]
        public int HzkInstance { get; set; }
    }
}