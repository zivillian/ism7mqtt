using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Protocol
{
    [XmlRoot("direct-logon-response")]
    public class LoginResp:IResponse
    {
        [XmlAttribute("state")]
        public LoginState State { get;set; }

        [XmlAttribute("sid")]
        public string Sid { get;set; }

        [XmlElement("errormsg")]
        public string Errormessage { get;set; }

        [XmlElement("installationname")]
        public string InstallationName { get;set; }

        [XmlElement("serialnumber")]
        public string Serialnumber { get;set; }

        [XmlElement("date-time")]
        public string DateTime { get;set; }

        [XmlElement("ism-softwareversion")]
        public string IsmSoftwareVersion { get;set; }

        [XmlElement("ism-hardwareversion")]
        public string IsmHardwareVersion { get;set; }

        [XmlElement("wlan-connected")]
        public bool IsWLanConnected { get;set; }

        [XmlElement("type")]
        public string Type { get;set; }

        public PayloadType MessageType => PayloadType.DirectLogonResp;
    }
}