using System.Collections.Generic;
using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Protocol
{
    [XmlRoot("read-systemconfig-response")]
    public class SystemconfigResp:IResponse
    {
        [XmlAttribute("sid")]
        public string Sid { get; set; }

        [XmlElement("busconfig")]
        public Busconfig BusConfig { get; set; }

        [XmlRoot("busconfig")]
        public class Busconfig
        {
            [XmlAttribute("type")]
            public string Type { get; set; }

            [XmlArray("busDevices")]
            [XmlArrayItem("busDevice")]
            public List<BusDevice> Devices { get; set; }
        }

        public class BusDevice
        {
            [XmlAttribute("ba")]
            public string Ba { get; set; }

            [XmlAttribute("sv")]
            public string SoftwareVersion { get; set; }

            [XmlAttribute("sr")]
            public string SoftwareRevision { get; set; }

            [XmlAttribute("cfg")]
            public string Configuration { get; set; }

            [XmlAttribute("did")]
            public string DeviceId { get; set; }
        }

        public PayloadType MessageType => PayloadType.SystemconfigResp;
    }
}