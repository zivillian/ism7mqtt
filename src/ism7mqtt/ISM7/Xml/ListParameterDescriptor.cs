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

        public override KeyValuePair<string,JsonNode> GetValues(ConverterTemplateBase converter)
        {
            var value = converter.GetValue();
            var result = new KeyValuePair<string, JsonNode>(SafeName, value);
            if (String.IsNullOrEmpty(KeyValueList)) return result;
            var names = KeyValueList.Split(';');
            var key = value.ToString();
            var index = Array.IndexOf(names, key);
            if (index < 0) return result;
            index++;
            if (index >= names.Length) return result;
            var data = new JsonObject
            {
                ["value"] = value,
                ["text"] = names[index]
            };
            return new KeyValuePair<string, JsonNode>(SafeName, data);
        }

        public JsonValue GetValue(JsonValue value)
        {
            if (String.IsNullOrEmpty(KeyValueList)) return value;
            var names = KeyValueList.Split(';');
            var name = value.ToString();
            var index = Array.IndexOf(names, name);
            if (index < 1 || index % 2 == 0) return value;
            index--;
            return JsonValue.Create(names[index]);
        } 
    }
}