using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using ism7mqtt.ISM7.Protocol;
using Newtonsoft.Json.Linq;

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

        public override JValue GetValue()
        {
            if (!HasValue)
                throw new InvalidOperationException();
            var result = new JValue(_bit.Value == OnValue);
            _bit = null;
            return result;
        }

        public override IEnumerable<InfoWrite> GetWrite(JValue value)
        {
            throw new NotImplementedException($"CTID '{CTID}' is not yet implemented");
        }
    }
}