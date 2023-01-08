using System;
using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Protocol
{
    [XmlRoot("irs")]
	public class InfonumberReadResp
    {
        [XmlAttribute("se")]
		public string Seq { get; set; }

		[XmlAttribute("ba")]
		public string BusAddress { get; set; }

		[XmlAttribute("st")]
		public TelegrResponseState State { get; set; }

		[XmlAttribute("in")]
		public ushort InfoNumber { get; set; }

		[XmlAttribute("dl")]
		public string DBLow { get; set; }

		[XmlAttribute("dh")]
		public string DBHigh { get; set; }

		[XmlAttribute("snr")]
		public string ServiceNumberAsText { get; set; }

        [XmlIgnore]
        public int? ServiceNumber => Int32.TryParse(ServiceNumberAsText, out var value) ? value : (int?)null;
    }
}