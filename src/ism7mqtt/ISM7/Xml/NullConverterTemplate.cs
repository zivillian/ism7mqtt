using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Newtonsoft.Json.Linq;

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

        public override JValue GetValue()
        {
            throw new NotImplementedException();
        }
    }
}