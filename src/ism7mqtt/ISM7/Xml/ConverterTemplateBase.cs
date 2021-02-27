using System.Collections.Generic;
using System.Xml.Serialization;
using Newtonsoft.Json.Linq;

namespace ism7mqtt.ISM7.Xml
{
    public abstract class ConverterTemplateBase
    {
        [XmlAttribute("CTID")]
        public int CTID { get; set; }

        [XmlElement("Type")]
        public string Type { get; set; }

        public abstract IEnumerable<ushort> TelegramIds { get; }

        public abstract void AddTelegram(ushort telegram, byte low, byte high);

        public abstract bool IsImplemented { get; }

        public abstract bool HasValue { get; }

        public abstract JValue GetValue();
    }
}