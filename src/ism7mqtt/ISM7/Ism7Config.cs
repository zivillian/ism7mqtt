using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using ism7mqtt.ISM7.Protocol;
using ism7mqtt.ISM7.Xml;

namespace ism7mqtt
{
    public class Ism7Config
    {
        private readonly IReadOnlyList<DeviceTemplate> _deviceTemplates;
        private readonly IReadOnlyList<ConverterTemplateBase> _converterTemplates;
        private readonly IReadOnlyList<ParameterDescriptor> _parameterTemplates;
        private readonly IDictionary<string, Device> _devices;
        private readonly ISet<int> _whitelist;

        public Ism7Config()
        {
            _deviceTemplates = LoadDeviceTemplates();
            _converterTemplates = LoadConverterTemplates();
            _parameterTemplates = LoadParameterTemplates();
            _whitelist = new HashSet<int>();
            if (File.Exists("parameter.txt"))
            {
                _whitelist = File.ReadAllLines("parameter.txt")
                    .Select(x=>
                    {
                        var b = Int32.TryParse(x, out var y);
                        return new {b, y};
                    })
                    .Where(x=>x.b)
                    .Select(x=>x.y)
                    .ToImmutableHashSet();
            }
            _devices = new Dictionary<string, Device>();
        }

        private List<DeviceTemplate> LoadDeviceTemplates()
        {
            var serializer = new XmlSerializer(typeof(DeviceTemplateConfig));
            using (var reader = new StringReader(Resources.DeviceTemplates))
            {
                var config = (DeviceTemplateConfig)serializer.Deserialize(reader);
                return config.DeviceTemplates;
            }
        }

        private List<ConverterTemplateBase> LoadConverterTemplates()
        {
            var serializer = new XmlSerializer(typeof(ConverterTemplateConfig));
            using (var reader = new StringReader(Resources.ConverterTemplates))
            {
                var converter = (ConverterTemplateConfig)serializer.Deserialize(reader);
                return converter.ConverterTemplates;
            }
        }

        private List<ParameterDescriptor> LoadParameterTemplates()
        {
            var serializer = new XmlSerializer(typeof(ParameterTemplateConfig));
            using (var reader = new StringReader(Resources.ParameterTemplates))
            {
                var converter = (ParameterTemplateConfig)serializer.Deserialize(reader);
                return converter.ParameterList;
            }
        }

        public void AddDevice(string ip, string ba, string did, string version)
        {
            var devices = _deviceTemplates.Where(x => x.WRSDeviceIds == did).ToList();
            DeviceTemplate device;
            if (devices.Count == 1)
                device = devices[0];
            else
            {
                device = devices.Single(x => x.SoftwareNumber == version);
            }
            var ptids = device.ParameterReferenceList
                .Where(x => !x.ExpertOnly)
                .Select(x => x.PTID)
                .Where(x=>_whitelist.Contains(x))
                .ToHashSet();
            _devices.Add(ba, new Device(device.Name, ip, ba, _parameterTemplates.Where(x => ptids.Contains(x.PTID)), _converterTemplates.Where(x => ptids.Contains(x.CTID))));
        }

        public IEnumerable<ushort> GetTelegramIdsForDevice(string ba)
        {
            var device = _devices[ba];
            return device.TelegramIds;
        }

        public IEnumerable<MqttMessage> ProcessData(IEnumerable<InfonumberReadResponse> data)
        {
            foreach (var value in data)
            {
                var device = _devices[value.BusAddress];
                device.ProcessDatapoint(value.InfoNumber, Converter.FromHex(value.DBLow), Converter.FromHex(value.DBHigh));
            }
            return _devices.Values.Select(x => x.Message).Where(x => x != null);
        }
        
        class Device
        {
            private readonly string _name;
            private readonly string _ip;
            private readonly string _ba;
            private readonly ImmutableDictionary<int, ParameterDescriptor> _parameter;
            private readonly ImmutableDictionary<ushort, List<ConverterTemplateBase>> _converter;

            public Device(string name, string ip, string ba, IEnumerable<ParameterDescriptor> parameter, IEnumerable<ConverterTemplateBase> converter)
            {
                _name = name;
                _ip = ip;
                _ba = ba;

                _parameter = parameter.ToImmutableDictionary(x => x.PTID);
                _converter = converter//.Where(x => x.IsImplemented)
                    .SelectMany(x => x.TelegramIds, (x, y) => new {Id = y, Value = x})
                    .ToLookup(x => x.Id, x => x.Value)
                    .ToImmutableDictionary(x=>x.Key, x=>x.ToList());
                EnsureUniqueParameterNames();
            }

            private void EnsureUniqueParameterNames()
            {
                var duplicates = _parameter.Values
                    .GroupBy(x => x.Name)
                    .Where(x => x.Count() > 1)
                    .ToList();
                foreach (var duplicate in duplicates.SelectMany(x=>x))
                {
                    duplicate.Name = $"{duplicate.Name}_{duplicate.PTID}";
                }
            }

            public IEnumerable<ushort> TelegramIds => _converter.Select(x=>x.Key);

            public void ProcessDatapoint(ushort telegram, byte low, byte high)
            {
                var converters = _converter[telegram];
                foreach (var converter in converters)
                {
                    converter.AddTelegram(telegram, low, high);
                }
            }

            public MqttMessage Message
            {
                get
                {
                    var converters = _converter.SelectMany(x=>x.Value)
                        .Where(x => x.HasValue)
                        .Distinct()
                        .ToList();
                    if (converters.Count == 0) return null;
                    var result = new MqttMessage( $"Wolf/{_name}_{_ip}_{_ba}");
                    foreach (var converter in converters)
                    {
                        var parameter = _parameter[converter.CTID];
                        result.AddProperty(parameter.GetValue(converter));
                    }
                    return result;
                }
            }
        }
    }
}