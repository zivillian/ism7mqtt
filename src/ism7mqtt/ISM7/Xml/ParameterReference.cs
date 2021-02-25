using System;
using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Xml
{
    public class ParameterReference
    {
        [XmlAttribute("PTID")]
        public int PTID { get; set; }

        [XmlAttribute("ExpertOnly")]
        public bool ExpertOnly { get; set; } = true;

        [XmlAttribute("IsDependent")]
        public bool IsDependent { get; set; } 

        [XmlAttribute("Preload")]
        public bool Preload { get; set; } 
    }
}
