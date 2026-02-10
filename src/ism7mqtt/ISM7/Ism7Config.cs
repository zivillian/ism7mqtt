using System;
using System.Collections.Generic;
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
        private readonly Ism7Localizer _localizer;
        private readonly IReadOnlyList<DeviceTemplate> _deviceTemplates;
        private readonly IReadOnlyList<ConverterTemplateBase> _converterTemplates;
        private readonly IReadOnlyList<ParameterDescriptor> _parameterTemplates;
        private readonly IDictionary<byte, List<RunningDevice>> _devices;
        private readonly Dictionary<string, List<InfoRead>> _bundles = new();
        private readonly ConfigRoot _config;

        public Ism7Config(string filename, Ism7Localizer localizer)
        {
            _localizer = localizer;
            _deviceTemplates = LoadDeviceTemplates();
            _converterTemplates = LoadConverterTemplates();
            _parameterTemplates = LoadParameterTemplates();
            if (File.Exists(filename))
            {
                _config = JsonSerializer.Deserialize(File.ReadAllText(filename), JsonContext.Default.ConfigRoot);
            }
            _devices = new Dictionary<byte, List<RunningDevice>>();
        }

        public int TcpPort => _config.TcpPort;

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

        public IEnumerable<string> AddAllDevices(string ip)
        {
            foreach (var ba in _config.Devices.Select(x=>x.ReadBusAddress).Distinct())
            {
                AddDevice(ip, ba);
                yield return ba;
            }
        }

        public void AddDevice(string ip, string ba)
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
                devices.Add(new RunningDevice(device.Name, ip, configDevice.ReadBusAddress, configDevice.WriteBusAddress, _parameterTemplates.Where(x => tids.Contains(x.PTID)), _converterTemplates.Where(x => tids.Contains(x.CTID)), _localizer));
            }
        }

        public IEnumerable<(string, List<InfoRead>)> GetBundlesForDevice(string ba)
        {
            var devices = _devices[Converter.FromHex(ba)];
            var bundles = new List<List<InfoRead>>();
            //https://github.com/zivillian/ism7mqtt/issues/177#issuecomment-3852657064
            //split into bundles of 20 info reads, but keep all info reads of a parameter in one bundle
            var parameters = devices.SelectMany(x => x.Parameters)
                .Where(x => x.InfoReadCount > 0)
                .OrderByDescending(x => x.InfoReadCount);
            foreach (var parameter in parameters)
            {
                var bundle = bundles.FirstOrDefault(x => x.Count <= 20 - parameter.InfoReadCount);
                if (bundle is null)
                {
                    bundles.Add(parameter.InfoReads.ToList());
                }
                else
                {
                    bundle.AddRange(parameter.InfoReads);
                }
            }

            foreach (var bundle in bundles)
            {
                var bundleId = (_bundles.Count + 1).ToString();
                _bundles.Add(bundleId, bundle);
                yield return (bundleId, bundle);
            }
        }

        public List<InfoRead> GetBundle(string bundleId)
        {
            return _bundles[bundleId];
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

        public IEnumerable<InfoWrite> GetWriteRequest(string mqttTopic, ReadOnlyMemory<string> propertyParts, string value)
        {
            foreach (var device in _devices.Values.SelectMany(x => x))
            {
                if (device.MqttTopic == mqttTopic)
                {
                    return device.GetWriteRequest(propertyParts, value);
                }
            }
            return Array.Empty<InfoWrite>();
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

        public IEnumerable<RunningDevice> Devices => _devices.SelectMany(x => x.Value);

        public IEnumerable<JsonMessage> JsonMessages => _devices.Values
            .SelectMany(x => x)
            .Where(x => x.HasValue)
            .Select(x => x.JsonMessage);

        public IEnumerable<MqttMessage> MqttMessages => _devices.Values
            .SelectMany(x => x)
            .Where(x => x.HasValue)
            .SelectMany(x => x.MqttMessages);

        public class RunningDevice
        {
            private readonly List<RunningParameter> _parameter;

            public RunningDevice(string name, string ip, string readBusAddress, string writeBusAddress, IEnumerable<ParameterDescriptor> parameter, IEnumerable<ConverterTemplateBase> converter, Ism7Localizer localizer)
            {
                Name = name;
                IP = ip;

                WriteAddress = writeBusAddress ?? $"0x{(Converter.FromHex(readBusAddress) - 5):X2}";
                MqttTopic = $"Wolf/{ip}/{name}_{readBusAddress}";

                _parameter = new List<RunningParameter>();
                foreach (var descriptor in parameter)
                {
                    var conv = converter.FirstOrDefault(x=>x.CTID == descriptor.PTID);
                    if (conv != null) _parameter.Add(new RunningParameter(descriptor, conv, localizer));
                }

                EnsureUniqueParameterNames();
            }

            public string MqttTopic { get; }

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

            public IEnumerable<RunningParameter> Parameters => _parameter;

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

            public IEnumerable<InfoWrite> GetWriteRequest(ReadOnlyMemory<string> propertyParts, string value)
            {
                var results = WritableParameters().SelectMany(x => x.GetWriteRequest(propertyParts, value));
                foreach (var result in results)
                {
                    result.BusAddress = WriteAddress;
                    result.Seq = "";
                    yield return result;
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
                                value.AsObject().Add(prop.Key, prop.Value.Deserialize(JsonContext.Default.JsonNode));
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
                            property.AddPrefix(MqttTopic);
                            yield return property;
                        }
                    }
                }
            }
        }

        public class RunningParameter
        {
            private readonly ParameterDescriptor _descriptor;
            private readonly Ism7Localizer _localizer;
            private readonly ConverterTemplateBase _converter;

            public RunningParameter(ParameterDescriptor descriptor, ConverterTemplateBase converter, Ism7Localizer localizer)
            {
                _descriptor = descriptor;
                _localizer = localizer;
                _converter = converter.Clone();
            }

            public string Name => _descriptor.Name;

            public string MqttName => MqttEscape(Name);

            public bool IsDuplicate { get; set; }

            public bool IsWritable => _descriptor.IsWritable;

            public bool HasValue => _converter.HasValue;

            public int InfoReadCount => _converter.InfoReadCount;

            public IEnumerable<InfoRead> InfoReads => _converter.InfoReads;

            public ParameterDescriptor Descriptor => _descriptor;

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

            public IEnumerable<InfoWrite> GetWriteRequest(ReadOnlyMemory<string> propertyParts, string value)
            {
                var parts = propertyParts.Span;
                if (!IsWritable) return Array.Empty<InfoWrite>();
                if (parts.IsEmpty) return Array.Empty<InfoWrite>();
                if (MqttName != parts[0]) return Array.Empty<InfoWrite>();
                parts = parts.Slice(1);
                if (IsDuplicate)
                {
                    if (parts.IsEmpty) return Array.Empty<InfoWrite>();
                    if (_descriptor.PTID.ToString() != parts[0]) return Array.Empty<InfoWrite>();
                    parts = parts.Slice(1);
                }
                if (_descriptor is ListParameterDescriptor listDescriptor)
                {
                    if (parts.IsEmpty) return Array.Empty<InfoWrite>();
                    if (parts[0] == "text")
                    {
                        var names = listDescriptor.KeyValueList.Split(';');
                        var candidates = _localizer.Revert(value);
                        bool validName = false;
                        foreach (var name in candidates)
                        {
                            for (int i = 1; i < names.Length; i += 2)
                            {
                                if (names[i] == name)
                                {
                                    value = names[i - 1];
                                    validName = true;
                                    break;
                                }
                            }

                            if (validName) break;
                        }
                        if (!validName) return Array.Empty<InfoWrite>();
                    }
                    else if (parts[0] != "value")
                    {
                        return Array.Empty<InfoWrite>();
                    }
                }
                return _converter.GetWrite(value);
            }

            public IEnumerable<InfoWrite> GetWriteRequest(string name, JsonNode node)
            {
                if (!IsWritable) return Array.Empty<InfoWrite>();
                if (Name != name) return Array.Empty<InfoWrite>();
                if (!TryGetWriteValue(node, out var value)) return Array.Empty<InfoWrite>();
                return _converter.GetWrite(value);
            }

            public bool TryGetWriteValue(JsonNode node, out string value)
            {
                value = node is JsonValue ? node.AsValue().ToString() : null;
                if (IsDuplicate)
                {
                    if (node is not JsonObject jobject) return false;
                    if (!jobject.TryGetPropertyValue(_descriptor.PTID.ToString(), out node)) return false;
                    value = node is JsonValue ? node.AsValue().ToString() : null;
                }
                if (_descriptor is ListParameterDescriptor listDescriptor)
                {
                    if (!String.IsNullOrEmpty(listDescriptor.KeyValueList))
                    {
                        if (node is JsonObject jobject)
                        {
                            if (jobject.TryGetPropertyValue("value", out var number))
                            {
                                value = number.AsValue().ToString();
                            }
                            else if (jobject.TryGetPropertyValue("text", out var text))
                            {
                                var names = listDescriptor.KeyValueList.Split(';');
                                var candidates = _localizer.Revert(text.ToString());
                                bool found = false;
                                foreach (var name in candidates)
                                {
                                    for (int i = 1; i < names.Length; i += 2)
                                    {
                                        if (names[i] == name)
                                        {
                                            value = names[i - 1];
                                            found = true;
                                            break;
                                        }
                                    }

                                    if (found) break;
                                }
                            }
                        }
                    }
                }
                return value is not null;
            }

            public KeyValuePair<string, JsonNode> GetJsonValue()
            {
                if (!_converter.HasValue)
                {
                    return new KeyValuePair<string, JsonNode>(Name, null);
                }
                JsonNode value = _converter.GetValue();
                if (_descriptor is ListParameterDescriptor listDescriptor)
                {
                    if (listDescriptor.Options.Any())
                    {
                        var key = value.ToString();
                        var text = listDescriptor.Options.Where(x => x.Key == key).Select(x => x.Value).FirstOrDefault();
                        if (!String.IsNullOrEmpty(text))
                        {
                            value = new JsonObject
                            {
                                ["value"] = value,
                                ["text"] = _localizer[text]
                            };
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
                if (!_converter.HasValue)
                {
                    return Array.Empty<MqttMessage>();
                }
                var json = _converter.GetValue();
                var text = JsonSerializer.Serialize(json, JsonContext.Default.JsonValue);
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
                    if (listDescriptor.Options.Any())
                    {
                        var key = value.Content;
                        var text = listDescriptor.Options.Where(x => x.Key == key).Select(x => x.Value).FirstOrDefault();
                        if (!String.IsNullOrEmpty(text))
                        {
                            yield return value
                                .Clone()
                                .AddSuffix("value");
                            yield return value
                                .Clone()
                                .AddSuffix("text")
                                .SetContent(_localizer[text]);
                        }
                        else
                        {
                            yield return value;
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
                    .Replace(' ', '_')
                    .Replace('+', '_')
                    .Replace('#', '_');
            }
        }
    }
}
