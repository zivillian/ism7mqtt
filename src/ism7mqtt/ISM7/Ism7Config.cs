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
using ism7mqtt.ISM7;

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
        private readonly string _parameterXmlOverridePath;

        public Ism7Config(string filename, Ism7Localizer localizer, string parameterXmlOverridePath = null)
        {
            _localizer = localizer;
            _parameterXmlOverridePath = parameterXmlOverridePath;
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
            var xml = String.IsNullOrEmpty(_parameterXmlOverridePath)
                ? Resources.ParameterTemplates
                : File.ReadAllText(_parameterXmlOverridePath);
            using (var reader = new StringReader(xml))
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
                var faultBusAddress = GetFaultBusAddress(configDevice);
                devices.Add(new RunningDevice(device.Name, ip, configDevice.ReadBusAddress, configDevice.WriteBusAddress, _parameterTemplates.Where(x => tids.Contains(x.PTID)), _converterTemplates.Where(x => tids.Contains(x.CTID)), _localizer)
                {
                    DeviceTemplateId = device.DTID,
                    FaultBusAddress = faultBusAddress,
                    // the module keeps one fault list per bus address, the first device with that fault list owns it
                    ReportsFaults = faultBusAddress.HasValue && !_devices.Values.SelectMany(x => x).Any(x => x.ReportsFaults && x.FaultBusAddress == faultBusAddress),
                });
            }
        }

        // Like Wolf (BusconfigMetaData.GetDeviceInfo), only the physical eBus devices have a fault list: the
        // boilers and heat pumps, BM, BM-2, RM-2, DFB, KM, MM, SM1, SM2 and CWL. The ISM, the virtual devices
        // (DHK, MK, ..._BM-2, ..._RM-2) and the Modbus devices (WRS) have none.
        private static readonly HashSet<int> FaultListDeviceTemplateIds = new()
        {
            30000, 40000, 50000, 60000, 70000, 80000, 90000, 100000, 110000, 120000, 130000, 140000, 150000, 160000,
            170000, 180000, 200000, 210000, 220000, 230000, 240000, 250000, 270000, 280000, 290000, 380000, 390000,
            400000, 430000, 440000, 450000, 460000,
        };

        /// <summary>The bus address of the device's fault list (Wolf's ErrorStateBusAddress), or null if it has none.</summary>
        private byte? GetFaultBusAddress(ConfigDevice device)
        {
            if (!FaultListDeviceTemplateIds.Contains(device.DeviceTemplateId)) return null;
            // the CWL and CWL-2 report their faults on 0x8B, not on their read address
            if (device.DeviceTemplateId is 230000 or 390000) return 0x8B;
            var readBusAddress = Converter.FromHex(device.ReadBusAddress);
            // Wolf drops the fault list of the first MM when there is a KM (BusconfigWriter.PatchErrorStateBusAddress)
            if (device.DeviceTemplateId == 40000 && readBusAddress == 0x51 && _config.Devices.Any(x => x.DeviceTemplateId == 50000)) return null;
            return readBusAddress;
        }

        private static string FormatBusAddress(byte busAddress) => $"0x{busAddress:X}";

        // the fault list addresses requested on this connection, null until they are selected
        private HashSet<byte> _requestedFaultBusAddresses;

        private readonly HashSet<string> _unknownFaultBusAddresses = new();

        /// <summary>
        /// The fault list addresses of all configured devices, written like the ISM reports them ("0x8").
        /// </summary>
        public IEnumerable<string> FaultBusAddresses => _config.Devices
            .Select(GetFaultBusAddress)
            .Where(x => x.HasValue)
            .Distinct()
            .Select(x => FormatBusAddress(x.Value));

        /// <summary>
        /// Selects the fault lists to request like Wolf does: only those of the configured devices that the ism7
        /// reports in its systemconfig. Only these devices publish a fault list.
        /// </summary>
        public IReadOnlyList<string> SelectFaultBusAddresses(IEnumerable<string> systemconfigBusAddresses)
        {
            var onBus = new HashSet<byte>();
            foreach (var address in systemconfigBusAddresses)
            {
                if (TryParseBusAddress(address, out var ba)) onBus.Add(ba);
            }
            _requestedFaultBusAddresses = _config.Devices
                .Where(x => onBus.Contains(Converter.FromHex(x.ReadBusAddress)))
                .Select(GetFaultBusAddress)
                .Where(x => x.HasValue)
                .Select(x => x.Value)
                .ToHashSet();
            return FaultBusAddresses.Where(x => _requestedFaultBusAddresses.Contains(Converter.FromHex(x))).ToList();
        }

        /// <summary>The device template id whose fault texts apply to the bus address, if it is configured.</summary>
        public int? GetFaultDeviceTemplateId(string busAddress)
        {
            if (!TryParseBusAddress(busAddress, out var ba)) return null;
            return _config.Devices
                .Where(x => GetFaultBusAddress(x) == ba)
                .Select(x => (int?)x.DeviceTemplateId)
                .FirstOrDefault();
        }

        /// <summary>
        /// Processes a fault list answer. Like Wolf, every answer is treated as a full snapshot of all requested
        /// devices: an entry missing from it stays in the device's list, but is no longer active. Codes outside
        /// 1-255 are no faults; each device keeps at most 10 entries, active ones first. Returns whether any device's list changed.
        /// </summary>
        public bool ProcessFaults(IEnumerable<ErrorStateResp> data)
        {
            var owners = _devices.Values.SelectMany(x => x)
                .Where(x => x.ReportsFaults && _requestedFaultBusAddresses is not null && _requestedFaultBusAddresses.Contains(x.FaultBusAddress.Value))
                .ToList();
            var entries = new List<(byte BusAddress, ErrorStateResp Entry)>();
            foreach (var entry in data)
            {
                if (entry.Code <= 0 || entry.Code > 255) continue;
                if (!TryParseBusAddress(entry.BusAddress, out var ba) || !owners.Any(x => x.FaultBusAddress == ba))
                {
                    // every answer repeats it, so it is logged once per connection
                    if (_unknownFaultBusAddresses.Add(entry.BusAddress ?? String.Empty))
                        Console.WriteLine($"fault list: ignoring entries for unknown bus address '{entry.BusAddress}'");
                    continue;
                }
                entries.Add((ba, entry));
            }
            var changed = false;
            foreach (var device in owners)
            {
                var faults = entries
                    .Where(x => x.BusAddress == device.FaultBusAddress)
                    .Select(x => x.Entry)
                    .OrderBy(x => x.Index)
                    .Select(x =>
                    {
                        var (text, textSource) = GetFaultText(x.Code, device.DeviceTemplateId);
                        return new FaultEntry(x.Index, x.Code, text,
                            String.IsNullOrEmpty(x.Start) ? null : x.Start, String.IsNullOrEmpty(x.Stop) ? null : x.Stop, x.Active)
                        {
                            TextSource = textSource,
                        };
                    });
                if (device.SetFaults(faults)) changed = true;
            }
            return changed;
        }

        private const int BmDeviceTemplateId = 30000;

        /// <summary>
        /// The text for the code on the device, and the device template id it was borrowed from. The BM also lists
        /// faults of the heat generator (live: codes 4 and 5 at 0x35), but Wolf has texts for them only under the
        /// heat generator's DTID. The BM's only own text (code 15, "... Wärmeerzeuger vom Netz getrennt") is the
        /// one Wolf shares with the heat generators, so a missing BM text is taken from the configured devices with
        /// that same code 15 text - only if they all agree on it.
        /// </summary>
        private (string Text, int? Source) GetFaultText(int code, int deviceTemplateId)
        {
            var text = FaultMessageTexts.Default.Get(code, deviceTemplateId);
            int? source = null;
            if (text is null && deviceTemplateId == BmDeviceTemplateId)
            {
                var heatGeneratorText = FaultMessageTexts.Default.Get(15, BmDeviceTemplateId);
                var borrowed = _config.Devices
                    .Select(x => x.DeviceTemplateId)
                    .Where(x => x != BmDeviceTemplateId && heatGeneratorText is not null && FaultMessageTexts.Default.Get(15, x) == heatGeneratorText)
                    .Distinct()
                    .Select(x => (Text: FaultMessageTexts.Default.Get(code, x), Source: x))
                    .Where(x => x.Text is not null)
                    .ToList();
                if (borrowed.Count > 0 && borrowed.All(x => x.Text == borrowed[0].Text))
                {
                    text = borrowed[0].Text;
                    source = borrowed[0].Source;
                }
            }
            return text is null ? (null, null) : (_localizer[text], source);
        }

        private static bool TryParseBusAddress(string value, out byte busAddress)
        {
            busAddress = 0;
            if (String.IsNullOrEmpty(value)) return false;
            try
            {
                busAddress = Converter.FromHex(value.Trim());
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
            {
                return false;
            }
        }

        public IEnumerable<(string, List<InfoRead>)> GetBundlesForDevice(string ba)
        {
            var devices = _devices[Converter.FromHex(ba)];
            var bundles = new List<List<InfoRead>>();
            //https://github.com/zivillian/ism7mqtt/issues/177#issuecomment-3852657064
            //split into bundles of 20 info reads, but keep all info reads of a parameter in one bundle
            //skip parameters we cannot convert (yet), every subscription counts against the limit of the ISM7
            //https://github.com/zivillian/ism7mqtt/issues/57#issuecomment-1874103810
            var parameters = devices.SelectMany(x => x.Parameters)
                .Where(x => x.InfoReadCount > 0 && x.ProducesValue)
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
                var bundleId = IdGenerator.GetNextBundleIdString();
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
            private readonly Ism7Localizer _localizer;

            public RunningDevice(string name, string ip, string readBusAddress, string writeBusAddress, IEnumerable<ParameterDescriptor> parameter, IEnumerable<ConverterTemplateBase> converter, Ism7Localizer localizer)
            {
                Name = name;
                IP = ip;
                _localizer = localizer;

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

            public bool HasValue => _parameter.Any(x => x.HasValue) || _faults is not null;

            public int DeviceTemplateId { get; init; }

            /// <summary>The bus address of the device's fault list, null if it has none.</summary>
            public byte? FaultBusAddress { get; init; }

            /// <summary>Whether this device publishes the fault list of its <see cref="FaultBusAddress"/>.</summary>
            public bool ReportsFaults { get; init; }

            public const string FaultTopic = "Stoermeldung";
            public const string FaultListTopic = "Stoermeldungen";
            public const string NoActiveFault = "Keine";
            private const int MaxFaults = 10;

            // null until the first fault list arrived, so nothing is published before the module answered
            private List<FaultEntry> _faults;

            public IReadOnlyList<FaultEntry> Faults => _faults;

            /// <summary>
            /// Takes the entries of a new answer. Like Wolf, an entry (code and start) missing from it is kept as
            /// history, no longer active, until the module reports it again or newer entries push it out of the
            /// last <see cref="MaxFaults"/>. Returns whether the list changed.
            /// </summary>
            public bool SetFaults(IEnumerable<FaultEntry> faults)
            {
                var list = faults.ToList();
                if (_faults is not null)
                {
                    var missing = _faults
                        .Where(x => !list.Any(y => y.Code == x.Code && y.Start == x.Start))
                        .Select(x => x with { Active = false })
                        .ToList();
                    list.AddRange(missing);
                }
                // at most MaxFaults entries in their order, the history never pushes out an active entry
                var keep = list.Select((x, i) => (x.Active, Position: i))
                    .OrderBy(x => x.Active ? 0 : 1)
                    .Take(MaxFaults)
                    .Select(x => x.Position)
                    .ToHashSet();
                list = list.Where((x, i) => keep.Contains(i)).ToList();
                if (_faults is not null && _faults.SequenceEqual(list)) return false;
                _faults = list;
                return true;
            }

            /// <summary>The newest active fault, e.g. "63 Funktionskontrolle Schwerkraftbremse", or <see cref="NoActiveFault"/>.</summary>
            public string ActiveFault
            {
                get
                {
                    var newest = _faults?
                        .Where(x => x.Active)
                        .OrderByDescending(x => x.StartTime ?? DateTime.MinValue)
                        .ThenBy(x => x.Index)
                        .FirstOrDefault();
                    return newest?.Label ?? _localizer[NoActiveFault];
                }
            }

            public JsonArray FaultListJson()
            {
                var result = new JsonArray();
                foreach (var fault in _faults ?? new List<FaultEntry>())
                {
                    var entry = new JsonObject
                    {
                        ["code"] = fault.Code,
                        ["text"] = fault.Text,
                        ["active"] = fault.Active,
                        ["start"] = fault.Start,
                        ["stop"] = fault.Stop,
                        ["index"] = fault.Index,
                    };
                    // the text was borrowed from this device template (a BM listing a heat generator fault)
                    if (fault.TextSource.HasValue) entry["text_source"] = fault.TextSource.Value;
                    result.Add(entry);
                }
                return result;
            }

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
                    if (parameters.Count == 0 && _faults is null) return null;
                    var result = new JsonObject();
                    if (_faults is not null)
                    {
                        result.Add(FaultTopic, ActiveFault);
                        result.Add(FaultListTopic, FaultListJson());
                    }
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
                    if (_faults is not null)
                    {
                        yield return new MqttMessage(FaultTopic, ActiveFault).AddPrefix(MqttTopic);
                        yield return new MqttMessage(FaultListTopic, JsonSerializer.Serialize(FaultListJson(), JsonContext.Default.JsonArray)).AddPrefix(MqttTopic);
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

            public bool ProducesValue => _converter.ProducesValue;

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