﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.RegularExpressions;
using ism7mqtt.ISM7.Xml;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using ism7mqtt.ISM7.Config;

namespace ism7mqtt.HomeAssistant
{
    public class HaDiscovery
    {
        private readonly Ism7Config _config;
        private readonly IMqttClient _mqttClient;
        private readonly string _discoveryId;
        private readonly Ism7Localizer _localizer;

        public bool EnableDebug { get; set; }

        public MqttQualityOfServiceLevel QosLevel { get; set; }

        public HaDiscovery(Ism7Config config, IMqttClient mqttClient, string discoveryId, Ism7Localizer localizer)
        {
            _config = config;
            _mqttClient = mqttClient;
            _discoveryId = discoveryId;
            _localizer = localizer;
        }

        private static readonly Dictionary<string, string> ExactStateMapping = CreateStateMapping();

        private static Dictionary<string, string> CreateStateMapping()
        {
            var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            void AddMapping(string localized, string haState)
            {
                if (!mapping.ContainsKey(localized))
                {
                    mapping[localized] = haState;
                }
            }

            // ON States
            AddMapping("Ein", "on"); AddMapping("开启", "on"); AddMapping("ON", "on"); AddMapping("SEES", "on");
            AddMapping("UKLJ.", "on"); AddMapping("IESLĒGT", "on"); AddMapping("ĮJ.", "on"); AddMapping("PORNIT", "on");
            AddMapping("On", "on"); AddMapping("Zał.", "on"); AddMapping("Zap", "on"); AddMapping("ZAP", "on");
            AddMapping("Вкл.", "on"); AddMapping("Til", "on"); AddMapping("Be", "on"); AddMapping("Açık", "on");
            AddMapping("Aan", "on"); AddMapping("ligada", "on");

            // OFF States
            AddMapping("Aus", "off"); AddMapping("关闭", "off"); AddMapping("OFF", "off"); AddMapping("Välja", "off");
            AddMapping("Isklj.", "off"); AddMapping("Izslēgts", "off"); AddMapping("Išj.", "off"); AddMapping("Oprit", "off");
            AddMapping("Off", "off"); AddMapping("Wył", "off"); AddMapping("Vyp", "off"); AddMapping("VYP", "off");
            AddMapping("Выкл.", "off"); AddMapping("Fra", "off"); AddMapping("Ki", "off"); AddMapping("Kapalı", "off");
            AddMapping("Uit", "off"); AddMapping("Desl.", "off");

            // ACTIVATED States
            AddMapping("Aktiviert", "on"); AddMapping("已激活", "on"); AddMapping("Ενεργοποιημένο", "on");
            AddMapping("Aktiveeritud", "on"); AddMapping("Uključeno", "on"); AddMapping("Aktivizēts", "on");
            AddMapping("Suaktyvinta", "on"); AddMapping("Activat", "on"); AddMapping("Attivato", "on");
            AddMapping("Activado", "on"); AddMapping("Activé", "on"); AddMapping("Włączone", "on");
            AddMapping("Aktivováno", "on"); AddMapping("Aktivovaný", "on"); AddMapping("Активировано", "on");
            AddMapping("Aktiveret", "on"); AddMapping("Aktiválva", "on"); AddMapping("Activated", "on");
            AddMapping("Etkinleştirildi", "on"); AddMapping("Geactiveerd", "on"); AddMapping("Ativado", "on");

            // DEACTIVATED States
            AddMapping("Deaktiviert", "off"); AddMapping("停用", "off"); AddMapping("Απενεργοποιημένο", "off");
            AddMapping("Deaktiveeritud", "off"); AddMapping("Isključeno", "off"); AddMapping("Deaktivizēts", "off");
            AddMapping("Išjungta", "off"); AddMapping("Dezactivat", "off"); AddMapping("Disattivato", "off");
            AddMapping("Desactivado", "off"); AddMapping("Désactivé", "off"); AddMapping("Nieaktywne", "off");
            AddMapping("Deaktivován", "off"); AddMapping("Deaktivovaný", "off"); AddMapping("Выключено", "off");
            AddMapping("Deaktiveret", "off"); AddMapping("Deaktiválva", "off"); AddMapping("Deactivated", "off");
            AddMapping("Devre dışı", "off"); AddMapping("Gedeactiveerd", "off"); AddMapping("Desativado", "off");

            return mapping;
        }


        public async Task PublishDiscoveryInfo(CancellationToken cancellationToken)
        {
            if (EnableDebug)
            {
                Console.WriteLine($"Publishing HA Discovery info for ID {_discoveryId}");
            }
            foreach (var message in GetDiscoveryInfo())
            {
                var data = JsonSerializer.Serialize(message.Content, JsonContext.Default.JsonObject);
                var builder = new MqttApplicationMessageBuilder()
                    .WithTopic(message.Path)
                    .WithPayload(data)
                    .WithQualityOfServiceLevel(QosLevel);
                var payload = builder
                    .Build();
                await _mqttClient.PublishAsync(payload, cancellationToken);
            }
        }

        public IEnumerable<JsonMessage> GetDiscoveryInfo()
        {
            return _config.Devices.SelectMany(GetDiscoveryInfo);
        }

        private string LaunderHomeassistantId(string id) {
            id = id.Replace("ä", "ae")
                .Replace("ö", "oe")
                .Replace("ü", "ue")
                .Replace("Ä", "Ae")
                .Replace("Ö", "Oe")
                .Replace("Ü", "Ue")
                .Replace("ß", "ss")
                .Replace(' ', '_');
            return Regex.Replace(id, "[^a-zA-Z0-9_ ]+", String.Empty, RegexOptions.Compiled);
        }

        private string ToHaObjectId(Ism7Config.RunningDevice device, ParameterDescriptor parameter)
        {
            var objectId = $"{_discoveryId}_{device.Name}_{device.WriteAddress}_{parameter.PTID}_{parameter.Name}";
            return LaunderHomeassistantId(objectId);
        }

        private IEnumerable<JsonMessage> GetDiscoveryInfo(Ism7Config.RunningDevice device)
        {
            List<JsonMessage> result = new List<JsonMessage>();
            foreach (var parameter in device.Parameters)
            {
                var descriptor = parameter.Descriptor;
                var type = GetHomeAssistantType(descriptor);
                if (type == null) continue;
                if (descriptor.ControlType == "DaySwitchTimes" || descriptor.ControlType.Contains("NO_DISPLAY")) continue;
                var uniqueId = ToHaObjectId(device, descriptor);

                // Handle parameters with name-duplicates - their ID will be part of the topic
                string deduplicator = "";
                string deduplicatorLabel = "";
                if (parameter.IsDuplicate)
                {
                    deduplicator = $"/{descriptor.PTID}";
                    deduplicatorLabel = $"_{descriptor.PTID}";
                }
                
                string discoveryTopic = $"homeassistant/{type}/{uniqueId}/config";
                var message = new JsonObject();
                message.Add("unique_id", uniqueId);

                var discoveryTopicSSuffix = GetDiscoveryTopicSuffix(descriptor);
                string stateTopic = $"{device.MqttTopic}/{parameter.MqttName}{deduplicator}{discoveryTopicSSuffix}";
                message.Add("state_topic", stateTopic);

                if (descriptor.IsWritable)
                {
                    string commandTopic = $"{device.MqttTopic}/set/{parameter.MqttName}{deduplicator}{discoveryTopicSSuffix}";
                    message.Add("command_topic", commandTopic);
                }

                message.Add("name", _localizer[descriptor.Name]);
                message.Add("default_entity_id", uniqueId);

                foreach (var (key, value) in GetDiscoveryProperties(descriptor))
                {
                    message.Add(key, value);
                }

                // Try to guess a suitable icon if none given
                if (!message.ContainsKey("icon"))
                {
                    if (descriptor.Name.ToLower().Contains("brenner"))
                        message.Add("icon", "mdi:fire");
                    else if (descriptor.Name.ToLower().Contains("solar"))
                        message.Add("icon", "mdi:solar-panel");
                    else if (descriptor.Name.ToLower().Contains("ventil"))
                        message.Add("icon", "mdi:pipe-valve");
                    else if (descriptor.Name.ToLower().Contains("heizung"))
                        message.Add("icon", "mdi:radiator");
                    else if (descriptor.Name.ToLower().Contains("pumpe"))
                        message.Add("icon", "mdi:pump");
                    if (descriptor.Name.ToLower().Contains("druck"))
                        message.Add("icon", "mdi:gauge");
                }

                message.Add("device", GetDiscoveryDeviceInfo(device));
                result.Add(new JsonMessage(discoveryTopic, message));
            }
            return result;
        }

        private JsonObject GetDiscoveryDeviceInfo(Ism7Config.RunningDevice device)
        {
            return new JsonObject
            {
                { "configuration_url", $"http://{device.IP}/" },
                { "manufacturer", "Wolf" },
                { "model", device.Name },
                { "name", $"{_discoveryId} {device.Name}" },
                {
                    "connections", new JsonArray
                    (
                        new JsonArray
                        (
                            "ip_dev",
                            $"{device.IP}_{device.Name}"
                        )
                    )
                }
            };
        }

        private string GetDiscoveryTopicSuffix(ParameterDescriptor descriptor)
        {
            if (descriptor is ListParameterDescriptor list)
            {
                return "/text";
            }
            else
            {
                return String.Empty;
            }
        }

        private string GetHomeAssistantType(ParameterDescriptor descriptor)
        {
            switch (descriptor)
            {
                case ListParameterDescriptor list:
                    // Try to auto-detect switch and select vs sensor and binary_sensor
                    if (list.IsWritable)
                    {
                        return list.IsBoolean ? "switch" : "select";
                    }

                    return list.IsBoolean ? "binary_sensor" : "sensor";
                case NumericParameterDescriptor numeric:
                    return numeric.IsWritable ? "number" : "sensor";
                case TextParameterDescriptor text:
                    return text.IsWritable ? "text" : "sensor";
                case OtherParameterDescriptor other:
                    return other.IsWritable ? "text" : "sensor";
                default:
                    return null;
            }
        }

        private IEnumerable<(string, JsonNode)> GetDiscoveryProperties(ParameterDescriptor descriptor)
        {
            switch (descriptor)
            {
                case NumericParameterDescriptor numeric:
                    if (numeric.IsWritable)
                    {
                        if (numeric.MinValueCondition != null)
                        {
                            if (Double.TryParse(numeric.MinValueCondition, NumberStyles.Number, CultureInfo.InvariantCulture, out var min))
                            {
                                yield return("min", min);
                            }
                            else if (EnableDebug)
                            {
                                Console.WriteLine($"Cannot parse MinValueCondition '{numeric.MinValueCondition}' for PTID {descriptor.PTID}");
                            }
                        }
                        if (numeric.MaxValueCondition != null)
                        {
                            if (Double.TryParse(numeric.MaxValueCondition, NumberStyles.Number, CultureInfo.InvariantCulture, out var max))
                            {
                                yield return ("max", max);
                            }
                            else if (EnableDebug)
                            {
                                Console.WriteLine($"Cannot parse MaxValueCondition '{numeric.MaxValueCondition}' for PTID {descriptor.PTID}");
                            }
                        }
                        if (numeric.StepWidth != null)
                            yield return ("step", numeric.StepWidth);
                    }
                    if (numeric.UnitName != null)
                    {
                        yield return ("unit_of_measurement", _localizer[numeric.UnitName]);
                        if (numeric.UnitName == "°C")
                        {
                            yield return ("icon", "mdi:thermometer");
                            yield return ("state_class", "measurement");
                        }
                        else if (numeric.UnitName == "%")
                        {
                            yield return ("state_class", "measurement");
                        }
                        else if (numeric.UnitName == "kWh")
                        {
                            yield return ("state_class", "total_increasing");
                            yield return ("device_class", "energy");
                        }
                        else if (numeric.UnitName == "W")
                        {
                            yield return ("state_class", "measurement");
                            yield return ("device_class", "power");
                        }
                        else if (numeric.UnitName == "kW")
                        {
                            yield return ("state_class", "measurement");
                            yield return ("device_class", "power");
                        }
                        else if (numeric.UnitName == "Hz")
                        {
                            yield return ("state_class", "measurement");
                            yield return ("device_class", "frequency");
                        }
                        else if (numeric.UnitName == "l/min")
                        {
                            yield return ("state_class", "measurement");
                        }
                        else if (numeric.UnitName == "L/min")
                        {
                            yield return ("state_class", "measurement");
                        }
                        else if (numeric.UnitName == "U/min")
                        {
                            yield return ("state_class", "measurement");
                        }
                    }
                    break;
                case ListParameterDescriptor list:
                // Define an exact mapping of localized state strings to HA states
                    if (list.IsBoolean)
                    {
                        foreach (var option in list.Options)
                        {
                            string localizedState = _localizer[option.Value] ?? option.Value;
                            if (EnableDebug) Console.WriteLine($"🔎 Checking: Raw='{option.Value}', Localized='{localizedState}'");
                            
                            if (ExactStateMapping.TryGetValue(localizedState, out var haState))
                            {
                                if (EnableDebug) Console.WriteLine($"✅ Mapping found: '{localizedState}' -> HA '{haState}'");
                                if (haState == "on")
                                    yield return ("payload_on", localizedState); // Ensure HA gets Hungarian value
                                else if (haState == "off")
                                    yield return ("payload_off", localizedState);
                            }
                            else
                            {
                                if (EnableDebug) Console.WriteLine($"⚠️ Warning: No mapping found for '{localizedState}'");
                            }
                        }
                    }
                    else
                    {
                        var options = new JsonArray();
                        foreach (var value in list.Options)
                        {
                            options.Add((JsonNode)_localizer[value.Value]);
                        }
                        yield return ("options", options);
                        yield return ("device_class", "enum");

                    }
                    break;
                default:
                    yield break;
            }
        }
    }
}