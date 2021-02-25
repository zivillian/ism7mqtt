using System.Collections.Generic;
using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Xml
{
    public abstract class MultiTelegramConverterTemplateBase : ConverterTemplateBase
    {
        [XmlArray("TelegramNumbers")]
        [XmlArrayItem("unsignedShort")]
        public List<ushort> TelegramNumbers { get; set; }
    }
}