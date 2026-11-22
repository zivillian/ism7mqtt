using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Protocol
{
    /// <summary>
    /// One entry of a device's fault list, answering <see cref="TelegramBundleReq.ErrorStateRead"/>.
    /// Entries with active=false are the device's fault history.
    /// </summary>
    [XmlRoot("ers")]
    public class ErrorStateResp
    {
        [XmlAttribute("se")]
        public string Seq { get; set; }

        [XmlAttribute("ba")]
        public string BusAddress { get; set; }

        [XmlAttribute("index")]
        public int Index { get; set; }

        [XmlAttribute("code")]
        public int Code { get; set; }

        [XmlAttribute("start")]
        public string Start { get; set; }

        [XmlAttribute("stop")]
        public string Stop { get; set; }

        [XmlAttribute("active")]
        public bool Active { get; set; }
    }
}