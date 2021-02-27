using System.Collections.Generic;
using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Protocol
{
    [XmlRoot("tbres")]
    public class TelegramBundleResp:IResponse
    {
        [XmlAttribute("ts")]
        public string Timestamp { get; set; }

        [XmlAttribute("gw")]
        public string GatewayId { get; set; }

        [XmlAttribute("bn")]
        public string BundleId { get; set; }

        [XmlAttribute("st")]
        public TelegrResponseState State { get; set; }
        
        [XmlAttribute("emsg")]
        public string Errormsg { get; set; }

        [XmlElement("irs")]
        public List<InfonumberReadResponse> InfonumberReadResponseTelegrams { get; set; }

        public PayloadType MessageType => PayloadType.TgrBundleResp;
    }
}