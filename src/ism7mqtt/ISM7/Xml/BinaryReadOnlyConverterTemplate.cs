using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Xml.Serialization;
using ism7mqtt.ISM7.Protocol;

namespace ism7mqtt.ISM7.Xml
{
    public class BinaryReadOnlyConverterTemplate : SingleTelegramConverterTemplateBase
    {
        private int? _bit;

        [XmlElement("OnValue")]
        public int OnValue { get; set; }

        [XmlElement("Bitnumber")]
        public int Bitnumber { get; set; }

        protected override void AddTelegram(byte low, byte high)
        {
            var value = (ushort) ((high << 8) | low);
            _bit = (value >> Bitnumber) & 0x1;
        }

        public override bool HasValue => _bit.HasValue;

        public override JsonValue GetValue()
        {
            if (!HasValue)
                throw new InvalidOperationException();
            var result = JsonValue.Create(_bit.Value == OnValue);
            _bit = null;
            return result;
        }

        public override IEnumerable<InfoWrite> GetWrite(string value)
        {
            throw new NotImplementedException($"CTID '{CTID}' is not yet implemented");
        }
    }
}