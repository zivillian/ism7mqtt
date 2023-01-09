using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
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
        private readonly IDictionary<byte, List<RunningDevice>> _devices;
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
            _devices = new Dictionary<byte, List<RunningDevice>>();
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
                devices = new List<RunningDevice>();
                _devices.Add(Converter.FromHex(ba), devices);
            }
            foreach (var configDevice in _config.Devices.Where(x => x.ReadBusAddress == ba))
            {
                var device = _deviceTemplates.First(x => x.DTID == configDevice.DeviceTemplateId);
                var tids = configDevice.Parameter.ToHashSet();
                devices.Add(new RunningDevice(device.Name, ip, ba, _parameterTemplates.Where(x => tids.Contains(x.PTID)), _converterTemplates.Where(x => tids.Contains(x.CTID))));
            }
            return true;
        }

        public IEnumerable<InfoRead> GetInfoReadForDevice(string ba)
        {
            var devices = _devices[Converter.FromHex(ba)];
            return devices.SelectMany(x => x.InfoReads);
        }

        public bool ProcessData(IEnumerable<InfonumberReadResp> data)
        {
            foreach (var value in data)
            {
                var devices = _devices[Converter.FromHex(value.BusAddress)];
                foreach (var device in devices)
                {
                    device.ProcessReadDatapoint(value.InfoNumber, value.ServiceNumber, Converter.FromHex(value.DBLow), Converter.FromHex(value.DBHigh));   
                }
            }
            return _devices.Values.SelectMany(x => x).Any(x => x.HasValue);
        }

        public bool ProcessData(IEnumerable<InfonumberWriteResp> data)
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
            return _devices.Values.SelectMany(x => x).Any(x => x.HasValue);
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

        public IEnumerable<JsonMessage> JsonMessages => _devices.Values
            .SelectMany(x => x)
            .Where(x => x.HasValue)
            .Select(x => x.JsonMessage);

        public IEnumerable<MqttMessage> MqttMessages => _devices.Values
            .SelectMany(x => x)
            .Where(x => x.HasValue)
            .SelectMany(x => x.MqttMessages);

        class RunningDevice
        {
            private readonly List<RunningParameter> _parameter;

            public RunningDevice(string name, string ip, string ba, IEnumerable<ParameterDescriptor> parameter, IEnumerable<ConverterTemplateBase> converter)
            {
                WriteAddress = $"0x{(Converter.FromHex(ba) - 5):X2}";
                Name = $"{name}_{ba}";
                IP = ip;

                _parameter = new List<RunningParameter>();
                foreach (var descriptor in parameter)
                {
                    var conv = converter.FirstOrDefault(x=>x.CTID == descriptor.PTID);
                    if (conv != null) _parameter.Add(new RunningParameter(descriptor, conv));
                }

                EnsureUniqueParameterNames();
            }

            public string MqttTopic => $"Wolf/{IP}/{Name}";

            public string Name { get; }

            public string IP { get; }

            public string WriteAddress { get; }

            public bool HasValue => _parameter.Any(x => x.HasValue);

            private void EnsureUniqueParameterNames()
            {
                var duplicates = _parameter
                    .GroupBy(x => x.Name)
                    .Where(x => x.Count() > 1)
                    .ToList();
                foreach (var duplicate in duplicates.SelectMany(x=>x))
                {
                    duplicate.IsDuplicate = true;
                }
            }

            public IEnumerable<InfoRead> InfoReads => _parameter.SelectMany(x => x.InfoReads);

            public IEnumerable<RunningParameter> WritableParameters()
            {
                return _parameter.Where(x => x.IsWritable);
            }

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
                foreach (var parameter in _parameter)
                {
                    parameter.ProcessDatapoint(telegram, service, low, high, write);
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
                return WritableParameters().SelectMany(x => x.GetWriteRequest(name, node));
            }

            public JsonMessage JsonMessage
            {
                get
                {
                    var parameters = _parameter
                        .Where(x => x.HasValue)
                        .Distinct()
                        .ToList();
                    if (parameters.Count == 0) return null;
                    var result = new JsonObject();
                    foreach (var parameter in parameters)
                    {
                        var property = parameter.GetJsonValue();
                        if (result.TryGetPropertyValue(property.Key, out var value))
                        {
                            foreach (var prop in property.Value.AsObject())
                            {
                                value.AsObject().Add(prop.Key, prop.Value.Deserialize<JsonNode>());
                            }
                        }
                        else
                        {
                            result.Add(property);
                        }
                    }
                    return new JsonMessage(MqttTopic, result);
                }
            }

            public IEnumerable<MqttMessage> MqttMessages
            {
                get 
                {
                    var parameters = _parameter
                        .Where(x => x.HasValue)
                        .Distinct();
                    foreach (var parameter in parameters)
                    {
                        var properties = parameter.GetSingleValues();
                        foreach (var property in properties)
                        {
                            property.AddPrefix(Name);
                            property.AddPrefix(IP);
                            property.AddPrefix("Wolf");
                            yield return property;
                        }
                    }
                }
            }
        }

        class RunningParameter
        {
            private readonly ParameterDescriptor _descriptor;
            private readonly ConverterTemplateBase _converter;

            public RunningParameter( ParameterDescriptor descriptor, ConverterTemplateBase converter)
            {
                _descriptor = descriptor;
                _converter = converter;
            }

            public string Name => _descriptor.Name;

            public string MqttName => MqttEscape(Name);

            public bool IsDuplicate { get; set; }

            public bool IsWritable => _descriptor.ReadOnlyConditionId == "False";

            public bool HasValue => _converter.HasValue;

            public IEnumerable<InfoRead> InfoReads => _converter.InfoReads;

            public void ProcessDatapoint(ushort telegram, int? service, byte low, byte high, bool write)
            {
                if (service.HasValue)
                {
                    if (write)
                    {
                        if (_converter.ServiceWriteNumber != service.Value) return;
                    }
                    else
                    {
                        if (_converter.ServiceReadNumber != service.Value) return;
                    }
                }
                if (!_converter.CanProcess(telegram)) return;
                _converter.AddTelegram(telegram, low, high);
            }

            public IEnumerable<InfoWrite> GetWriteRequest(string name, JsonNode node)
            {
                if (!IsWritable) return Array.Empty<InfoWrite>();
                if (Name != name) return Array.Empty<InfoWrite>();
                if (!TryGetWriteValue(node, out var value)) return Array.Empty<InfoWrite>();
                return _converter.GetWrite(value);
            }

            public bool TryGetWriteValue(JsonNode node, out JsonValue value)
            {
                value = node is JsonValue ? node.AsValue() : null;
                if (IsDuplicate)
                {
                    if (node is not JsonObject jobject) return false;
                    if (!jobject.TryGetPropertyValue(_descriptor.PTID.ToString(), out node)) return false;
                }
                if (_descriptor is ListParameterDescriptor listDescriptor)
                {
                    if (!String.IsNullOrEmpty(listDescriptor.KeyValueList))
                    {
                        if (node is JsonObject jobject)
                        {
                            if (jobject.TryGetPropertyValue("value", out var number))
                            {
                                value = number.AsValue();
                            }
                            else if (jobject.TryGetPropertyValue("text", out var text))
                            {
                                var names = listDescriptor.KeyValueList.Split(';');
                                var name = text.ToString();
                                for (int i = 1; i < names.Length; i += 2)
                                {
                                    if (names[i] == name)
                                    {
                                        value = JsonValue.Create(names[i - 1]);
                                        break;
                                    }
                                }

                            }
                        }
                    }
                }
                return value is not null;
            }

            public KeyValuePair<string, JsonNode> GetJsonValue()
            {
                JsonNode value = _converter.GetValue();
                if (_descriptor is ListParameterDescriptor listDescriptor)
                {
                    if (!String.IsNullOrEmpty(listDescriptor.KeyValueList))
                    {
                        var names = listDescriptor.KeyValueList.Split(';');
                        var key = value.ToString();
                        for (int i = 0; i < names.Length - 1; i += 2)
                        {
                            if (names[i] == key)
                            {
                                value = new JsonObject
                                {
                                    ["value"] = value,
                                    ["text"] = names[i + 1]
                                };
                                break;
                            }
                        }
                    }
                }
                if (IsDuplicate)
                {
                    value = new JsonObject
                    {
                        [_descriptor.PTID.ToString()] = value,
                    };
                }
                return new KeyValuePair<string,JsonNode>(Name, value);
            }

            public IEnumerable<MqttMessage> GetSingleValues()
            {
                var json = _converter.GetValue();
                var text = JsonSerializer.Serialize(json);
                var result = new MqttMessage(MqttName, text);
                if (IsDuplicate)
                {
                    result.AddSuffix(_descriptor.PTID.ToString());
                }
                return HandleListParameter(result);
            }

            private IEnumerable<MqttMessage> HandleListParameter(MqttMessage value)
            {
                if (_descriptor is ListParameterDescriptor listDescriptor)
                {
                    if (!String.IsNullOrEmpty(listDescriptor.KeyValueList))
                    {
                        var names = listDescriptor.KeyValueList.Split(';');
                        var key = value.ToString();
                        for (int i = 0; i < names.Length - 1; i += 2)
                        {
                            if (names[i] == key)
                            {
                                yield return value
                                    .Clone()
                                    .AddSuffix("value");
                                yield return value
                                    .Clone()
                                    .AddSuffix("text")
                                    .SetContent(names[i + 1]);
                                break;
                            }
                        }
                    }
                }
                else
                {
                    yield return value;
                }
            }

            private static string MqttEscape(string topic)
            {
                return topic
                    .Replace("ä", "ae")
                    .Replace("ö", "oe")
                    .Replace("ü", "ue")
                    .Replace("Ä", "Ae")
                    .Replace("Ö", "Oe")
                    .Replace("Ü", "Ue")
                    .Replace("ß", "ss")
                    .Replace('/', '_')
                    .Replace(' ', '_');
            }
        }
    }
}