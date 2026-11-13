using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Xml.Serialization;
using ism7mqtt.ISM7.Protocol;

namespace ism7mqtt.ISM7.Xml
{
    /// <summary>
    /// One day of a BM-2 time program. It is not read via info numbers but with a dedicated
    /// s03f1rd request (see <see cref="TimeprogRead"/>), which is only sent if reading time programs
    /// is enabled. The value is published read-only as text, e.g. "05:30-22:00, 17:00-18:00".
    /// </summary>
    public class Timeprog03F1ConverterTemplate:ConverterTemplateBase
    {
        private List<(int Start, int End)> _switchTimes;

        [XmlElement("DayOfWeek")]
        public byte DayOfWeek { get; set; }

        [XmlElement("HeatprogNumber")]
        public int HeatprogNumber { get; set; }

        [XmlElement("ProgramType")]
        public int ProgramType { get; set; }

        [XmlElement("HzkInstance")]
        public int HzkInstance { get; set; }

        public override int InfoReadCount => 0;

        public override IEnumerable<InfoRead> InfoReads => Array.Empty<InfoRead>();

        public override bool CanProcess(ushort telegram)
        {
            return false;
        }

        public override void AddTelegram(ushort telegram, byte low, byte high)
        {
        }

        public override bool HasValue => _switchTimes is not null;

        public TimeprogRead CreateRead(string busAddress)
        {
            return new TimeprogRead
            {
                BusAddress = busAddress,
                HeatprogNumber = HeatprogNumber,
                ProgramType = ProgramType,
                HzkInstance = HzkInstance,
                DayOfWeek = DayOfWeek
            };
        }

        public bool Matches(TimeprogReadResp response)
        {
            return response.HeatprogNumber == HeatprogNumber
                   && response.ProgramType == ProgramType
                   && response.HzkInstance == HzkInstance
                   && response.DayOfWeek == DayOfWeek;
        }

        public void SetData(string hex)
        {
            _switchTimes = Decode(hex);
        }

        /// <summary>
        /// db contains pairs of little endian uint16 (start, end) in minutes since midnight.
        /// Unused slots are 0000/0000 (read) or FFFF/FFFF (written by smartset).
        /// </summary>
        public static List<(int Start, int End)> Decode(string hex)
        {
            var data = Convert.FromHexString(hex ?? String.Empty);
            var result = new List<(int Start, int End)>();
            for (int i = 0; i + 3 < data.Length; i += 4)
            {
                var start = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i));
                var end = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i + 2));
                if (start == 0xFFFF || end == 0xFFFF) continue;
                if (start == end) continue;
                result.Add((start, end));
            }
            return result;
        }

        public static string Format(IEnumerable<(int Start, int End)> switchTimes)
        {
            var text = String.Join(", ", switchTimes.Select(x => $"{FormatTime(x.Start)}-{FormatTime(x.End)}"));
            return text.Length == 0 ? "-" : text;
        }

        private static string FormatTime(int minutes)
        {
            return $"{minutes / 60:00}:{minutes % 60:00}";
        }

        public override JsonValue GetValue()
        {
            if (!HasValue)
                throw new InvalidOperationException();
            // like all other converters: publish once per read
            var result = JsonValue.Create(Format(_switchTimes));
            _switchTimes = null;
            return result;
        }

        public override IEnumerable<InfoWrite> GetWrite(string value)
        {
            throw new NotImplementedException($"writing time programs (CTID '{CTID}') is not supported yet");
        }

        public override ConverterTemplateBase Clone()
        {
            return Clone(new Timeprog03F1ConverterTemplate
            {
                DayOfWeek = DayOfWeek,
                HeatprogNumber = HeatprogNumber,
                ProgramType = ProgramType,
                HzkInstance = HzkInstance,
            });
        }
    }
}
