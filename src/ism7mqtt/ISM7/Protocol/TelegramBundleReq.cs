using System.Collections.Generic;
using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Protocol
{
    [XmlRoot("tbreq")]
	public class TelegramBundleReq
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
    }
}