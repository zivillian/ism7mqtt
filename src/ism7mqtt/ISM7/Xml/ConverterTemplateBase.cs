using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Xml.Serialization;
using ism7mqtt.ISM7.Protocol;

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

        public abstract bool HasValue { get; }

        public abstract JsonValue GetValue();

        public abstract IEnumerable<InfoWrite> GetWrite(JsonValue value);
    }
}