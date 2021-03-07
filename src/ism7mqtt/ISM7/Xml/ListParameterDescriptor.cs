using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Newtonsoft.Json.Linq;

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

        public override IEnumerable<JProperty> GetValues(ConverterTemplateBase converter)
        {
            var value = converter.GetValue();
            yield return new JProperty(SafeName, value);
            if (String.IsNullOrEmpty(KeyValueList)) yield break;
            var names = KeyValueList.Split(';');
            var key = value.ToString();
            var index = Array.IndexOf(names, key);
            if (index < 0) yield break;
            index++;
            if (index >= names.Length) yield break;
            yield return new JProperty($"{SafeName}_Text", names[index]);
        }
    }
}