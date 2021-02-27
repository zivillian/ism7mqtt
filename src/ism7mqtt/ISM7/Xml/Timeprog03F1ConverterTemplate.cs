using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Newtonsoft.Json.Linq;

namespace ism7mqtt.ISM7.Xml
{
    public class Timeprog03F1ConverterTemplate:ConverterTemplateBase
    {
        [XmlElement("DayOfWeek")]
        public byte DayOfWeek { get; set; }

        [XmlElement("HeatprogNumber")]
        public int HeatprogNumber { get; set; }

        [XmlElement("ProgramType")]
        public int ProgramType { get; set; }

        [XmlElement("HzkInstance")]
        public int HzkInstance { get; set; }

        public override IEnumerable<ushort> TelegramIds => Array.Empty<ushort>();

        public override void AddTelegram(ushort telegram, byte low, byte high)
        {
        }

        public override bool IsImplemented => false;

        public override bool HasValue => false;
        public override JValue GetValue()
        {
            throw new NotImplementedException();
        }
    }
}