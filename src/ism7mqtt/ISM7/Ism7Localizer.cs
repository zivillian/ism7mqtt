using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace ism7mqtt;

public class Ism7Localizer
{
    private Dictionary<string, string> _translations;
    private Dictionary<string, string[]> _reverse;

    private static readonly string[] _validLanguages =
    [
        "CHN",
        "GRC",
        "EST",
        "HRV",
        "LVA",
        "LTU",
        "ROU",
        "ITA",
        "ESP",
        "FRA",
        "POL",
        "CZE",
        "SVK",
        "RUS",
        "DNK",
        "HUN",
        "GBR",
        "TUR",
        "NLD",
        "BUL",
        "POR"
    ];

    public Ism7Localizer(string language)
    {
        if (language == "DEU")
        {
            _translations = new Dictionary<string, string>(0);
            _reverse = new Dictionary<string, string[]>(0);
            return;
        }
        if (!_validLanguages.Contains(language)) throw new ArgumentOutOfRangeException(nameof(language));

        var xdoc = XDocument.Load(new StringReader(Resources.Dictionary));
        _translations = xdoc.Elements("TextTable")
            .Elements("TableEntries")
            .Elements("TextTableEntry")
            .ToDictionary(x => (string)x.Element("DEU"), x => (string)x.Element(language));
        _reverse = _translations.GroupBy(x => x.Value).ToDictionary(x => x.Key, x => x.Select(y => y.Key).ToArray());
    }

    public string this[string text] => _translations.GetValueOrDefault(text, text);

    public IEnumerable<string> Revert(string translated) => _reverse.GetValueOrDefault(translated, [translated]);
}