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

        public override IEnumerable<(string, JsonNode)> DiscoveryProperties {
            get
            {
                if (IsWritable)
                {
                    if (MinValueCondition != null)
                        yield return("min", Double.Parse(MinValueCondition));
                    if (MaxValueCondition != null)
                        yield return ("max", Double.Parse(MaxValueCondition));
                    if (StepWidth != null)
                        yield return ("step", StepWidth);
                }
                if (UnitName != null)
                {
                    yield return ("unit_of_measurement", UnitName);
                    if (UnitName == "°C")
                    {
                        yield return ("icon", "mdi:thermometer");
                        yield return ("state_class", "measurement");
                    }
                    else if (UnitName == "%")
                    {
                        yield return ("state_class", "measurement");
                    }
                }
            }
        }
    }
}