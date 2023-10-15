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

        [XmlElement("ServiceReadNumber")]
        public int? ServiceReadNumber { get; set; }

        [XmlElement("ServiceWriteNumber")]
        public int ServiceWriteNumber { get; set; }

        public abstract IEnumerable<InfoRead> InfoReads { get; }

        public abstract void AddTelegram(ushort telegram, byte low, byte high);

        public abstract bool CanProcess(ushort telegram);

        public abstract bool HasValue { get; }

        public abstract JsonValue GetValue();

        public abstract IEnumerable<InfoWrite> GetWrite(string value);

        public abstract ConverterTemplateBase Clone();

        protected ConverterTemplateBase Clone(ConverterTemplateBase clone)
        {
            clone.CTID = CTID;
            clone.Type = Type;
            clone.ServiceReadNumber = ServiceReadNumber;
            clone.ServiceWriteNumber = ServiceWriteNumber;
            return clone;
        }
    }
}