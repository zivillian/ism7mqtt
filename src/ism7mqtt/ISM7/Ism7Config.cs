using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using ism7mqtt.ISM7.Xml;

namespace ism7mqtt
{
    public class Ism7Config
    {
        private readonly IReadOnlyList<DeviceTemplate> _devices;
        private readonly IReadOnlyList<ConverterTemplateBase> _converter;

        public Ism7Config()
        {
            _devices = LoadDevices();
            _converter = LoadConverter();
        }

        private List<DeviceTemplate> LoadDevices()
        {
            var serializer = new XmlSerializer(typeof(DeviceTemplateConfig));
            using (var reader = new StringReader(Resources.DeviceTemplates))
            {
                var config = (DeviceTemplateConfig)serializer.Deserialize(reader);
                return config.DeviceTemplates;
            }
        }

        private List<ConverterTemplateBase> LoadConverter()
        {
            var serializer = new XmlSerializer(typeof(ConverterTemplateConfig));
            using (var reader = new StringReader(Resources.ConverterTemplates))
            {
                var converter = (ConverterTemplateConfig)serializer.Deserialize(reader);
                return converter.ConverterTemplates;
            }
        }
    }
}