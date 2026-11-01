using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace ism7mqtt.ISM7
{
    /// <summary>
    /// Wolf's fault texts (Resources/fault_messages.xml). The same code means different things on different
    /// devices, so a text is looked up by code and device template id (DTID).
    /// </summary>
    public class FaultMessageTexts
    {
        private static FaultMessageTexts _default;

        private readonly Dictionary<(int Code, int Dtid), string> _texts = new();

        public FaultMessageTexts(string xml)
        {
            var doc = XDocument.Load(new StringReader(xml));
            foreach (var message in doc.Descendants("FaultMessage"))
            {
                if (!Int32.TryParse((string)message.Attribute("code"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)) continue;
                var text = (string)message.Attribute("text");
                var dtids = ((string)message.Attribute("dtids") ?? String.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var dtid in dtids)
                {
                    if (Int32.TryParse(dtid, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                        _texts.TryAdd((code, id), text);
                }
            }
        }

        public static FaultMessageTexts Default => _default ??= new FaultMessageTexts(Resources.FaultMessages);

        /// <summary>The text for the code on that device template, or null when Wolf defines none.</summary>
        public string Get(int code, int deviceTemplateId)
        {
            return _texts.TryGetValue((code, deviceTemplateId), out var text) ? text : null;
        }
    }

    /// <summary>One entry of a device's fault list as published to MQTT.</summary>
    public record FaultEntry(int Index, int Code, string Text, string Start, string Stop, bool Active)
    {
        private static readonly string[] TimeFormats =
        {
            "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy HH:mm", "d.M.yyyy H:mm:ss", "d.M.yyyy H:mm",
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm",
        };

        /// <summary>"63 Funktionskontrolle Schwerkraftbremse", or only the code when there is no text for it.</summary>
        public string Label => Text is null ? Code.ToString(CultureInfo.InvariantCulture) : $"{Code} {Text}";

        /// <summary>The device template id the text was borrowed from, null when it is the device's own text.</summary>
        public int? TextSource { get; init; }

        /// <summary>The start time, if the module reported it in a known format.</summary>
        public DateTime? StartTime => DateTime.TryParseExact(Start, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;
    }
}