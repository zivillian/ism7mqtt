using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Xml
{
    public class TextParameterDescriptor:ParameterDescriptor
    {
        [XmlElement("MinValue")]
        public int MinValue { get; set; }

        [XmlElement("MaxValue")]
        public int MaxValue { get; set; }

        public override string HomeAssistantType => IsWritable ? "text" : "sensor";

        public override IDictionary<string, JsonNode> DiscoveryProperties => null;
    }
}