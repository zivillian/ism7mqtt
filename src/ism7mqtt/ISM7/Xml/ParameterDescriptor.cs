using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Xml.Serialization;

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

        public bool IsDuplicate { get; set; }

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

        public KeyValuePair<string,JsonNode> GetValues(ConverterTemplateBase converter)
        {
            var value = GetValueCore(converter);
            if (IsDuplicate)
            {
                value = new JsonObject
                {
                    [PTID.ToString()] = value,
                };
            }
            return new KeyValuePair<string,JsonNode>(SafeName, value);
        }

        protected virtual JsonNode GetValueCore(ConverterTemplateBase converter)
        {
            return converter.GetValue();
        }

        public bool TryGetValue(JsonNode value, out JsonValue converted)
        {
            if (IsDuplicate)
            {
                converted = null;
                if (value is not JsonObject jobject) return false;
                if (!jobject.TryGetPropertyValue(PTID.ToString(), out value)) return false;
            }
            converted = GetWrite(value);
            return converted is not null;
        }

        protected virtual JsonValue GetWrite(JsonNode node)
        {
            if (node is JsonValue)
                return node.AsValue();
            return null;
        }
    }
}