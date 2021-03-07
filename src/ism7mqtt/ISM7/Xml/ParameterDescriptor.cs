using System;
using System.Collections.Generic;
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

        public string SafeName
        {
            get
            {
                return Name.Replace("ä", "ae")
                    .Replace("ö", "oe")
                    .Replace("ü", "ue")
                    .Replace("Ä", "Ae")
                    .Replace("Ö", "Oe")
                    .Replace("Ü", "Ue")
                    .Replace("ß", "ss")
                    ;
            }
        }

        public virtual IEnumerable<JProperty> GetValues(ConverterTemplateBase converter)
        {
            var value = converter.GetValue();
            yield return new JProperty(SafeName, value);
        }
    }
}