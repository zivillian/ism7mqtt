using System;
using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Protocol
{
    [XmlRoot("iwr")]
    public class InfoWrite
    {
        [XmlAttribute("se")]
        public string Seq { get; set; } = String.Empty;

        [XmlAttribute("ba")]
        public string BusAddress { get; set; }

        [XmlAttribute("in")]
        public ushort InfoNumber { get; set; }

        [XmlAttribute("dl")]
        public string DBLow { get; set; }

        [XmlAttribute("dh")]
        public string DBHigh { get; set; }

        [XmlAttribute("snr")]
        public int ServiceNumber { get; set; } = -1;

        public bool ShouldSerializeServiceNumber()
        {
            return ServiceNumber >= 0;
        }
    }
}