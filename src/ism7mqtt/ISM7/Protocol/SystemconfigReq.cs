using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Protocol
{
    [XmlRoot("read-systemconfig-request")]
    public class SystemconfigReq:IPayload
    {
        [XmlAttribute("sid")]
        public string Sid { get; set; }
        public PayloadType Type => PayloadType.SystemconfigReq;
    }
}