using System.Collections.Generic;
using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Protocol
{
    [XmlRoot("tbreq")]
	public class TelegramBundleReq:XmlPayload
	{
		[XmlAttribute("bn")]
		public string BundleId { get; set; }

		[XmlAttribute("gw")]
		public string GatewayId { get; set; }

		[XmlAttribute("ae")]
		public bool AbortOnError { get; set; }

		[XmlAttribute("ty")]
		public TelegramBundleType TelegramBundleType { get; set; }

		[XmlElement("ird")]
		public List<InfoRead> InfoReadTelegrams { get; set; }

		[XmlElement("iwr")]
		public List<InfoWrite> InfoWriteTelegrams { get; set; }

        [XmlElement("erd")]
        public ErrorStateRead EStRead { get; set; }

        public override PayloadType Type => PayloadType.TgrBundleReq;

        public class ErrorStateRead
        {
            [XmlAttribute("se")]
            public string Seq { get; set; }

            [XmlAttribute("li")]
            public string VisibleDeviceAdresses { get; set; }
        }
    }
}