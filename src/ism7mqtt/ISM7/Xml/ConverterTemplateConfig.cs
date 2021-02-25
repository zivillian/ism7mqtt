using System.Collections.Generic;
using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Xml
{
    [XmlRoot("ConverterTemplateConfig")]
    public class ConverterTemplateConfig
    {
        [XmlArray("ConverterTemplates")]
        [XmlArrayItem("NumericConverter16Template", typeof(NumericConverter16Template))]
        [XmlArrayItem("DateTimeConverterTemplate", typeof(DateTimeConverterTemplate))]
        [XmlArrayItem("MixerStateConverterTemplate", typeof(MixerStateConverterTemplate))]
        [XmlArrayItem("READONLY_Bit4to7ConverterTemplate", typeof(Bit4to7ConverterTemplate))]
        [XmlArrayItem("READONLY_Bit0to3ConverterTemplate", typeof(Bit0to3ConverterTemplate))]
        [XmlArrayItem("SolarStatisticConverterTemplate", typeof(SolarStatisticConverterTemplate))]
        [XmlArrayItem("BinaryReadOnlyConverterTemplate", typeof(BinaryReadOnlyConverterTemplate))]
        [XmlArrayItem("NumericConverter32Template", typeof(NumericConverter32Template))]
        [XmlArrayItem("BM2TimeConverterTemplate", typeof(BM2TimeConverterTemplate))]
        [XmlArrayItem("NullConverterTemplate", typeof(NullConverterTemplate))]
        [XmlArrayItem("BM2DateConverterTemplate", typeof(BM2DateConverterTemplate))]
        [XmlArrayItem("TimeprogConverterTemplate", typeof(TimeprogConverterTemplate))]
        [XmlArrayItem("Timeprog03F1ConverterTemplate", typeof(Timeprog03F1ConverterTemplate))]
        [XmlArrayItem("UnicodeTextConverter", typeof(UnicodeTextConverter))]
        public List<ConverterTemplateBase> ConverterTemplates { get; set; }
    }
}