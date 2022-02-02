using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using ism7mqtt.ISM7.Config;
using ism7mqtt.ISM7.Protocol;
using ism7mqtt.ISM7.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ism7mqtt
{
    public class Ism7Config
    {
        private readonly IReadOnlyList<DeviceTemplate> _deviceTemplates;
        private readonly IReadOnlyList<ConverterTemplateBase> _converterTemplates;
        private readonly IReadOnlyList<ParameterDescriptor> _parameterTemplates;
        private readonly IDictionary<string, Device> _devices;
        private readonly ConfigRoot _config;

        public Ism7Config(string filename)
        {
            _deviceTemplates = LoadDeviceTemplates();
            _converterTemplates = LoadConverterTemplates();
            _parameterTemplates = LoadParameterTemplates();
            if (File.Exists(filename))
            {
                _config = JsonConvert.DeserializeObject<ConfigRoot>(File.ReadAllText(filename));
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

        public bool AddDevice(string ip, string ba)
        {
            var tids = _config.Devices
                .Where(x => x.ReadBusAddress == ba)
                .Select(x => new {Dtid = x.DeviceTemplateId, Ptids = x.Parameter.ToHashSet()})
                .FirstOrDefault();
            if (tids is null) return false;
            var device = _deviceTemplates.First(x => x.DTID == tids.Dtid);
            _devices.Add(ba, new Device(device.Name, ip, ba, _parameterTemplates.Where(x => tids.Ptids.Contains(x.PTID)), _converterTemplates.Where(x => tids.Ptids.Contains(x.CTID))));
            return true;
        }

        public IEnumerable<ushort> GetTelegramIdsForDevice(string ba)
        {
            var device = _devices[ba];
            return device.TelegramIds;
        }

        public IEnumerable<MqttMessage> ProcessData(IEnumerable<InfonumberReadResp> data)
        {
            foreach (var value in data)
            {
                var device = _devices[value.BusAddress];
                device.ProcessDatapoint(value.InfoNumber, Converter.FromHex(value.DBLow), Converter.FromHex(value.DBHigh));
            }
            return _devices.Values.Select(x => x.Message).Where(x => x != null);
        }

        public IEnumerable<MqttMessage> ProcessData(IEnumerable<InfonumberWriteResp> data)
        {
            foreach (var value in data)
            {
                var device = _devices.Values.First(x => x.WriteAddress == value.BusAddress);
                device.ProcessDatapoint(value.InfoNumber, Converter.FromHex(value.DBLow), Converter.FromHex(value.DBHigh));
            }
            return _devices.Values.Select(x => x.Message).Where(x => x != null);
        }

        public IEnumerable<InfoWrite> GetWriteRequest(string mqttTopic, JObject data)
        {
            foreach (var device in _devices.Values)
            {
                if (device.MqttTopic == mqttTopic)
                {
                    return device.GetWriteRequest(data);
                }
            }
            return Array.Empty<InfoWrite>();
        }
        
        class Device
        {
            private readonly ImmutableDictionary<int, ParameterDescriptor> _parameter;
            private readonly ImmutableDictionary<ushort, List<ConverterTemplateBase>> _converter;

            public Device(string name, string ip, string ba, IEnumerable<ParameterDescriptor> parameter, IEnumerable<ConverterTemplateBase> converter)
            {
                WriteAddress = $"0x{(Converter.FromHex(ba) - 5):X2}";
                MqttTopic = $"Wolf/{ip}/{name}_{ba}";

                _parameter = parameter.ToImmutableDictionary(x => x.PTID);
                _converter = converter
                    .SelectMany(x => x.TelegramIds, (x, y) => new {Id = y, Value = x})
                    .ToLookup(x => x.Id, x => x.Value)
                    .ToImmutableDictionary(x=>x.Key, x=>x.ToList());
                EnsureUniqueParameterNames();
            }

            public string MqttTopic { get; }

            public string WriteAddress { get; }

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

            public IEnumerable<ParameterDescriptor> WritableParameters()
            {
                return _parameter.Values.Where(x => x.ReadOnlyConditionId == "False");
            }

            public void ProcessDatapoint(ushort telegram, byte low, byte high)
            {
                var converters = _converter[telegram];
                foreach (var converter in converters)
                {
                    converter.AddTelegram(telegram, low, high);
                }
            }

            public IEnumerable<InfoWrite> GetWriteRequest(JObject data)
            {
                foreach (var property in data.Properties())
                {
                    if (property.Value is JValue value)
                    {
                        var results = GetWriteRequest(property.Name, value);
                        foreach (var result in results)
                        {
                            result.BusAddress = WriteAddress;
                            result.Seq = "";
                            yield return result;
                        }
                    }
                }
            }

            private IEnumerable<InfoWrite> GetWriteRequest(string name, JValue value)
            {
                var parameter = WritableParameters().FirstOrDefault(x => x.SafeName == name);
                if (parameter is null) return Array.Empty<InfoWrite>();
                var converter = _converter.Values.SelectMany(x => x).FirstOrDefault(x => x.CTID == parameter.PTID);
                if (converter is null) return Array.Empty<InfoWrite>();
                if (parameter is ListParameterDescriptor listParameter)
                {
                    value = listParameter.GetValue(value);
                }
                return converter.GetWrite(value);
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
                    var result = new MqttMessage(MqttTopic);
                    foreach (var converter in converters)
                    {
                        var parameter = _parameter[converter.CTID];
                        foreach (var value in parameter.GetValues(converter))
                        {
                            result.AddProperty(value);
                        }
                    }
                    return result;
                }
            }
        }
    }
}