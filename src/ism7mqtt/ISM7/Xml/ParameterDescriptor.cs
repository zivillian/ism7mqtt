using System;
using System.Xml.Serialization;
using Newtonsoft.Json.Linq;

namespace ism7mqtt.ISM7.Xml
{
    public abstract class ParameterDescriptor
    {
        [XmlAttribute("PTID")]
        public int PTID { get; set; }

        [XmlElement("Name")]
        public string Name { get; set; }

        [XmlElement("RemoveConditionId")]
        public string RemoveConditionId { get; set; }

        [XmlElement("ReadOnlyConditionId")]
        public string ReadOnlyConditionId { get; set; }

        [XmlElement("InactiveConditionId")]
        public string InactiveConditionId { get; set; }

        [XmlElement("IsSnapshotTransmitEnabled")]
        public string IsSnapshotTransmitEnabled { get; set; }

        [XmlElement("ControlType")]
        public string ControlType { get; set; }

        public JProperty GetValue(ConverterTemplateBase converter)
        {
            var value = converter.GetValue();
            return new JProperty(Name, value);
        }
    }
}