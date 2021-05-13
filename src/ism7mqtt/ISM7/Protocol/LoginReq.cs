using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Protocol
{
    [XmlRoot("direct-logon-request")]
    public class LoginReq:XmlPayload
    {
        [XmlElement("password")]
        public string Password { get;set; }

        public override PayloadType Type => PayloadType.DirectLogonReq;
    }
}