using System;
using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Protocol
{
    [XmlRoot("ird")]
    public class InfoRead
    {
        [XmlAttribute("in")]
        public int InfoNumber { get; set; }
        
        [XmlAttribute("se")]
        public string Seq { get; set; }

        [XmlAttribute("ba")]
        public string BusAddress { get; set; }

        [XmlAttribute("is")]
        public int Intervall { get; set; } = -1;

        public bool ShouldSerializeIntervall()
        {
            return Intervall >= 0;
        }
    }
}