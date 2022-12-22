using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using System.Text.Json.Nodes;

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

        protected override JsonNode GetValueCore(ConverterTemplateBase converter)
        {
            var value = converter.GetValue();
            var key = value.ToString();
            // some list types have a binary/bool converter, even if it doesn't make much sense.. try to detect those cases
            if (!IsBoolean && converter is BinaryReadOnlyConverterTemplate)
            {
                if (value.TryGetValue<bool>(out var valueBool))
                {
                    key = valueBool ? "1" : "0";
                }
            }

            var text = Options.Where(x => x.Key == key).Select(x => x.Value).FirstOrDefault();
            if (!String.IsNullOrEmpty(text))
            {
                return new JsonObject
                {
                    ["value"] = value,
                    ["text"] = text
                };
            }
            return value;
        }

        protected override JsonValue GetWrite(JsonNode node)
        {
            if (String.IsNullOrEmpty(KeyValueList)) return base.GetWrite(node);
            if (node is not JsonObject jobject) return base.GetWrite(node);
            if (jobject.TryGetPropertyValue("value", out var value))
            {
                return value.AsValue();
            }
            if (jobject.TryGetPropertyValue("text", out value))
            {
                var names = KeyValueList.Split(';');
                var name = value.ToString();
                for (int i = 1; i < names.Length; i += 2)
                {
                    if (names[i] == name) return JsonValue.Create(names[i - 1]);
                }
            }
            return base.GetWrite(node);
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