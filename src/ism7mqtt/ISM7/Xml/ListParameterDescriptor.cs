using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Xml
{
    public class ListParameterDescriptor:ParameterDescriptor
    {
        [XmlElement("App")]
        public bool App { get; set; }

        [XmlElement("MinValueCondition")]
        public string MinValueCondition { get; set; }

        [XmlElement("MaxValueCondition")]
        public string MaxValueCondition { get; set; }

        [XmlElement("KeyValueList")]
        public string KeyValueList { get; set; }

        [XmlElement("DependentDefinitionId")]
        public string DependentDefinitionId { get; set; }

        private IReadOnlyCollection<KeyValuePair<string, string>> _options;
        public IReadOnlyCollection<KeyValuePair<string, string>> Options
        {
            get
            {
                if (_options is not null) return _options;
                if (String.IsNullOrEmpty(KeyValueList))
                {
                    _options = Array.Empty<KeyValuePair<string, string>>();
                }
                else
                {
                    var result = new List<KeyValuePair<string, string>>();
                    var names = KeyValueList.Split(";");
                    for (int i = 0; i < names.Length - 1; i += 2) {
                        result.Add(new KeyValuePair<string,string>(names[i], names[i + 1]));
                    }
                    _options = result;
                }
                return _options;
            }
        }

        private bool? _isBoolean;
        public bool IsBoolean
        {
            get
            {
                if (_isBoolean.HasValue) return _isBoolean.Value;
                if (Options.Count != 2)
                {
                    _isBoolean = false;
                }
                else if (Options.Any(x => x.Value == "Ein") && Options.Any(x => x.Value == "Aus"))
                {
                    _isBoolean = true;
                }
                else if (Options.Any(x => x.Value == "Aktiviert") && Options.Any(x => x.Value == "Deaktiviert"))
                {
                    _isBoolean = true;
                }
                else
                {
                    _isBoolean = false;
                }
                return _isBoolean.Value;
            }
        }
    }
}