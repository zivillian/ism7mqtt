using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Protocol
{
    [XmlRoot("s03f2rs")]
    public class TimeprogReadResp
    {
        [XmlAttribute("se")]
        public string Seq { get; set; }

        [XmlAttribute("ba")]
        public string BusAddress { get; set; }

        [XmlAttribute("hp")]
        public int HeatprogNumber { get; set; }

        [XmlAttribute("ty")]
        public int ProgramType { get; set; }

        [XmlAttribute("hk")]
        public int HzkInstance { get; set; }

        [XmlAttribute("d")]
        public int DayOfWeek { get; set; }

        /// <summary>
        /// hex encoded switch times: pairs of little endian uint16 (start, end) in minutes since midnight,
        /// unused slots are 0000/0000 or FFFF/FFFF
        /// </summary>
        [XmlAttribute("db")]
        public string Data { get; set; }

        [XmlAttribute("st")]
        public TelegrResponseState State { get; set; }
    }
}
