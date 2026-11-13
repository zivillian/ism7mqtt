using System;
using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Protocol
{
    /// <summary>
    /// Reads one day of a BM-2 time program (eBus service 0x03F1). The ISM7 answers with
    /// <see cref="TimeprogReadResp"/> (s03f2rs). Format taken from smartset traces in
    /// https://github.com/zivillian/ism7mqtt/issues/83
    /// </summary>
    [XmlRoot("s03f1rd")]
    public class TimeprogRead
    {
        [XmlAttribute("se")]
        public string Seq { get; set; } = String.Empty;

        [XmlAttribute("ba")]
        public string BusAddress { get; set; }

        /// <summary>time program number (1-3)</summary>
        [XmlAttribute("hp")]
        public int HeatprogNumber { get; set; }

        /// <summary>0 = heating, 1 = hot water, 2 = circulation</summary>
        [XmlAttribute("ty")]
        public int ProgramType { get; set; }

        [XmlAttribute("hk")]
        public int HzkInstance { get; set; }

        /// <summary>0 = Sunday ... 6 = Saturday</summary>
        [XmlAttribute("d")]
        public int DayOfWeek { get; set; }
    }
}
