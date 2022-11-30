using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Xml.Serialization;
using ism7mqtt.ISM7.Protocol;

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
            Console.Error.WriteLine($"Timeprog03F1ConverterTemplate: T:{telegram} H:{high} L:{low}");
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