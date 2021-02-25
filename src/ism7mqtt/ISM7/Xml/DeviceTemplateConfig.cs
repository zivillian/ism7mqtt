using System.Collections.Generic;
using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Xml
{
    [XmlRoot("DeviceTemplateConfig")]
    public class DeviceTemplateConfig
    {
        [XmlArray("DeviceTemplates")]
        [XmlArrayItem("DeviceTemplate")]
        public List<DeviceTemplate> DeviceTemplates { get; set; }
    }
}