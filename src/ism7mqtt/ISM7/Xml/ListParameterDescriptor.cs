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

        protected override JsonNode GetValueCore(ConverterTemplateBase converter)
        {
            var value = converter.GetValue();
            if (String.IsNullOrEmpty(KeyValueList)) return value;
            var names = KeyValueList.Split(';');
            var key = value.ToString();
            for (int i = 0; i < names.Length - 1; i += 2)
            {
                if (names[i] == key)
                {
                    return new JsonObject
                    {
                        ["value"] = value,
                        ["text"] = names[i + 1]
                    };
                }
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
    }
}