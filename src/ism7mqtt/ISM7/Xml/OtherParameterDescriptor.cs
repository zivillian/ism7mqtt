using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Xml
{
    public class OtherParameterDescriptor:ParameterDescriptor
    {
        [XmlElement("App")]
        public bool App { get; set; }

        [XmlElement("Decimals")]
        public int Decimals { get; set; }

        public override string HomeAssistantType => null;
        public override IDictionary<string, JsonNode> DiscoveryProperties => null;
    }
}