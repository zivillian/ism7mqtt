using System.Collections.Generic;
using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Xml
{
    public class DeviceTemplate
    {
        [XmlAttribute("DTID")]
        public int DTID { get; set; }

        [XmlAttribute("Name")]
        public string Name { get; set; }

        [XmlElement("CustomerId")]
        public string CustomerId { get; set; }

        [XmlElement("SoftwareNumber")]
        public string SoftwareNumber { get; set; }

        [XmlElement("WRSDeviceIds")]
        public string WRSDeviceIds { get; set; }

        [XmlElement("EnableEBus5InfoNumberTelegram")]
        public bool EnableEBus5InfoNumberTelegram { get; set; }

        [XmlElement("HasOwnFaultMessageHistory")]
        public bool HasOwnFaultMessageHistory { get; set; }

        [XmlArray("ParameterReferenceList")]
        [XmlArrayItem("ParameterReference")]
        public List<ParameterReference> ParameterReferenceList { get; set; }
    }
}