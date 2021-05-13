using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Protocol
{
    [XmlRoot("read-systemconfig-request")]
    public class SystemconfigReq:XmlPayload
    {
        [XmlAttribute("sid")]
        public string Sid { get; set; }
        public override PayloadType Type => PayloadType.SystemconfigReq;
    }
}