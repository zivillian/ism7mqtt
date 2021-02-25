using System;
using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Protocol
{
    [XmlRoot("ird")]
    public class InfoRead
    {
        [XmlAttribute("in")]
        public int[] InfoNumber { get; set; }
        
        [XmlAttribute("se")]
        public string Seq { get; set; }

        [XmlAttribute("ba")]
        public string BusAddress { get; set; }
    }
}