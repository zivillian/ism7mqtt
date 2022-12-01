using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Xml.Serialization;
using ism7mqtt.ISM7.Protocol;

namespace ism7mqtt.ISM7.Xml
{
    public class NullConverterTemplate : ConverterTemplateBase
    {
        [XmlElement("XMLName")]
        public string XMLName { get; set; }

        [XmlElement("IntervalSec")]
        public int IntervalSec { get; set; }

        public override IEnumerable<ushort> TelegramIds => Array.Empty<ushort>();

        public override void AddTelegram(ushort telegram, byte low, byte high)
        {
            throw new NotImplementedException($"CTID '{CTID}' is not yet implemented");
        }

        public override bool HasValue => false;

        public override JsonValue GetValue()
        {
            throw new NotImplementedException($"CTID '{CTID}' is not yet implemented");
        }

        public override IEnumerable<InfoWrite> GetWrite(JsonValue value)
        {
            throw new NotImplementedException($"CTID '{CTID}' is not yet implemented");
        }
    }
}