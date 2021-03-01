using System;
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
        public double StepWidth { get; set; }

        [XmlElement("Decimals")]
        public int Decimals { get; set; }
    }
}