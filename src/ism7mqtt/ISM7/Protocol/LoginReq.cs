using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Protocol
{
    [XmlRoot("direct-logon-request")]
    public class LoginReq:IPayload
    {
        [XmlElement("password")]
        public string Password { get;set; }

        public PayloadType Type => PayloadType.DirectLogonReq;
    }
}