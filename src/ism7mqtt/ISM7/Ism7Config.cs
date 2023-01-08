using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Serialization;
using ism7mqtt.ISM7.Config;
using ism7mqtt.ISM7.Protocol;
using ism7mqtt.ISM7.Xml;

namespace ism7mqtt
{
    public class Ism7Config
    {
        private readonly IReadOnlyList<DeviceTemplate> _deviceTemplates;
        private readonly IReadOnlyList<ConverterTemplateBase> _converterTemplates;
        private readonly IReadOnlyList<ParameterDescriptor> _parameterTemplates;
        private readonly IDictionary<byte, List<Device>> _devices;
        private readonly ConfigRoot _config;

        public Ism7Config(string filename)
        {
            _deviceTemplates = LoadDeviceTemplates();
            _converterTemplates = LoadConverterTemplates();
            _parameterTemplates = LoadParameterTemplates();
            if (File.Exists(filename))
            {
                _config = JsonSerializer.Deserialize<ConfigRoot>(File.ReadAllText(filename));
            }
            _devices = new Dictionary<byte, List<Device>>();
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
            if (!_devices.TryGetValue(Converter.FromHex(ba), out var devices))
            {
                devices = new List<Device>();
                _devices.Add(Converter.FromHex(ba), devices);
            }
            foreach (var configDevice in _config.Devices.Where(x => x.ReadBusAddress == ba))
            {
                var device = _deviceTemplates.First(x => x.DTID == configDevice.DeviceTemplateId);
                var tids = configDevice.Parameter.ToHashSet();
                devices.Add(new Device(device.Name, ip, ba, _parameterTemplates.Where(x => tids.Contains(x.PTID)), _converterTemplates.Where(x => tids.Contains(x.CTID))));
            }
            return true;
        }

        public IEnumerable<InfoRead> GetInfoReadForDevice(string ba)
        {
            var devices = _devices[Converter.FromHex(ba)];
            return devices.SelectMany(x => x.InfoReads);
        }

        public IEnumerable<MqttMessage> ProcessData(IEnumerable<InfonumberReadResp> data)
        {
            foreach (var value in data)
            {
                var devices = _devices[Converter.FromHex(value.BusAddress)];
                foreach (var device in devices)
                {
                    device.ProcessReadDatapoint(value.InfoNumber, value.ServiceNumber, Converter.FromHex(value.DBLow), Converter.FromHex(value.DBHigh));   
                }
            }
            return _devices.Values.SelectMany(x => x).Select(x => x.Message).Where(x => x != null);
        }

        public IEnumerable<MqttMessage> ProcessData(IEnumerable<InfonumberWriteResp> data)
        {
            foreach (var value in data)
            {
                var devices = _devices.Values.SelectMany(x => x)
                    .Where(x => Converter.FromHex(x.WriteAddress) == Converter.FromHex(value.BusAddress))
                    .ToList();
                foreach (var device in devices)
                {
                    device.ProcessWriteDatapoint(value.InfoNumber, value.ServiceNumber, Converter.FromHex(value.DBLow), Converter.FromHex(value.DBHigh));   
                }
            }
            return _devices.Values.SelectMany(x => x).Select(x => x.Message).Where(x => x != null);
        }

        public IEnumerable<InfoWrite> GetWriteRequest(string mqttTopic, JsonObject data)
        {
            foreach (var device in _devices.Values.SelectMany(x => x))
            {
                if (device.MqttTopic == mqttTopic)
                {
                    return device.GetWriteRequest(data);
                }
            }
            return Array.Empty<InfoWrite>();
        }

        public IEnumerable<Device> Devices => _devices.SelectMany(x => x.Value);

        public class Device
        {
            private readonly ImmutableDictionary<int, ParameterDescriptor> _parameter;
            private readonly ImmutableList<ConverterTemplateBase> _converter;

            public Device(string name, string ip, string ba, IEnumerable<ParameterDescriptor> parameter, IEnumerable<ConverterTemplateBase> converter)
            {
                Name = name;
                Ip = ip;

                WriteAddress = $"0x{(Converter.FromHex(ba) - 5):X2}";
                MqttTopic = $"Wolf/{ip}/{name}_{ba}";

                _parameter = parameter.ToImmutableDictionary(x => x.PTID);
                _converter = converter.ToImmutableList();
                EnsureUniqueParameterNames();
            }

            public string MqttTopic { get; }

            public string WriteAddress { get; }

            public string Name { get; }

            public string Ip { get; }

            private void EnsureUniqueParameterNames()
            {
                var duplicates = _parameter.Values
                    .GroupBy(x => x.Name)
                    .Where(x => x.Count() > 1)
                    .ToList();
                foreach (var duplicate in duplicates.SelectMany(x=>x))
                {
                    duplicate.IsDuplicate = true;
                }
            }

            public IEnumerable<InfoRead> InfoReads => _converter.SelectMany(x => x.InfoReads);

            public IEnumerable<ParameterDescriptor> WritableParameters()
            {
                return _parameter.Values.Where(x => x.IsWritable);
            }

            public IEnumerable<ParameterDescriptor> Parameters => _parameter.Values;

            public void ProcessReadDatapoint(ushort telegram, int? service, byte low, byte high)
            {
                ProcessDatapoint(telegram, service, low, high, false);
            }

            public void ProcessWriteDatapoint(ushort telegram, int? service, byte low, byte high)
            {
                ProcessDatapoint(telegram, service, low, high, true);
            }

            private void ProcessDatapoint(ushort telegram, int? service, byte low, byte high, bool write)
            {
                foreach (var converter in _converter)
                {
                    if (service.HasValue)
                    {
                        if (write)
                        {
                            if (converter.ServiceWriteNumber != service.Value) continue;
                        }
                        else
                        {
                            if (converter.ServiceReadNumber != service.Value) continue;
                        }
                    }
                    if (!converter.CanProcess(telegram)) continue;
                    converter.AddTelegram(telegram, low, high);
                }
            }

            public IEnumerable<InfoWrite> GetWriteRequest(JsonObject data)
            {
                foreach (var property in data)
                {
                    var results = GetWriteRequest(property.Key, property.Value);
                    foreach (var result in results)
                    {
                        result.BusAddress = WriteAddress;
                        result.Seq = "";
                        yield return result;
                    }
                }
            }

            private IEnumerable<InfoWrite> GetWriteRequest(string name, JsonNode node)
            {
                foreach (var parameter in WritableParameters().Where(x => x.SafeName == name))
                {
                    if (!parameter.TryGetValue(node, out var value)) continue;
                    foreach (var converter in _converter.Where(x => x.CTID == parameter.PTID))
                    {
                        foreach (var write in converter.GetWrite(value))
                        {
                            yield return write;
                        }
                    }
                }
            }

            public MqttMessage Message
            {
                get
                {
                    var converters = _converter
                        .Where(x => x.HasValue)
                        .Distinct()
                        .ToList();
                    if (converters.Count == 0) return null;
                    var result = new MqttMessage(MqttTopic);
                    foreach (var converter in converters)
                    {
                        var parameter = _parameter[converter.CTID];
                        result.AddProperty(parameter.GetValues(converter));
                    }
                    return result;
                }
            }
        }
    }
}