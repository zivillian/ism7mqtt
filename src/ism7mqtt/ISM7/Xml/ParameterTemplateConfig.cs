using System.Collections.Generic;
using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Xml
{
    [XmlRoot("ParameterTemplateConfig")]
    public class ParameterTemplateConfig
    {
        [XmlArray("ParameterList")]
        [XmlArrayItem("NumericParameterDescriptor", typeof(NumericParameterDescriptor))]
        [XmlArrayItem("ListParameterDescriptor", typeof(ListParameterDescriptor))]
        [XmlArrayItem("OtherParameterDescriptor", typeof(OtherParameterDescriptor))]
        [XmlArrayItem("TextParameterDescriptor", typeof(TextParameterDescriptor))]
        public List<ParameterDescriptor> ParameterList { get; set; }
    }
}