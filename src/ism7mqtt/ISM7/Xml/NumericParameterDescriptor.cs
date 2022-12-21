using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Xml
{
    public class NumericParameterDescriptor:ParameterDescriptor
    {
        [XmlElement("UnitName")]
        public string UnitName { get; set; }
        
        [XmlElement("App")]
        public bool App { get; set; }

        [XmlElement("MinValueCondition")]
        public string MinValueCondition { get; set; }

        [XmlElement("MaxValueCondition")]
        public string MaxValueCondition { get; set; }

        [XmlElement("StepWidth")]
        public double? StepWidth { get; set; }

        [XmlElement("Decimals")]
        public int Decimals { get; set; }

        public override string HomeAssistantType => IsWritable ? "number" : "sensor";

        public override IDictionary<string, JsonNode> DiscoveryProperties {
            get {
                Dictionary<string, JsonNode> result = new Dictionary<string, JsonNode>();
                if (IsWritable) {
                    if (MinValueCondition != null)
                        result.Add("min", Double.Parse(MinValueCondition));
                    if (MaxValueCondition != null)
                        result.Add("max", Double.Parse(MaxValueCondition));
                    if (StepWidth != null)
                        result.Add("step", StepWidth);
                }
                if (UnitName != null) {
                    result.Add("unit_of_measurement", UnitName);
                    if (UnitName == "°C")
                        result.Add("icon", "mdi:thermometer");
                }
                return result;
            }

        }
    }
}