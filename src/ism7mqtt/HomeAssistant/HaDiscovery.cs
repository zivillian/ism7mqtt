using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace ism7mqtt.HomeAssistant
{
    public class HaDiscovery
    {
        private readonly Ism7Config _config;

        public HaDiscovery(Ism7Config config)
        {
            _config = config;
        }

        public IEnumerable<MqttMessage> GetDiscoveryInfo(string discoveryId)
        {
            return _config.Devices.SelectMany(x => GetDiscoveryInfo(discoveryId, x));
        }

        private IEnumerable<MqttMessage> GetDiscoveryInfo(string discoveryId, Ism7Config.Device device)
        {
            List<MqttMessage> result = new List<MqttMessage>();
            foreach (var descriptor in device.Parameters)
            {
                string type = descriptor.HomeAssistantType;
                if (type == null) continue;
                if (descriptor.ControlType == "DaySwitchTimes") continue;

                // Handle parameters with name-duplicates - their ID will be part of the topic
                string deduplicator = "";
                string deduplicatorLabel = "";
                if (descriptor.IsDuplicate)
                {
                    deduplicator = $"/{descriptor.PTID}";
                    deduplicatorLabel = $"_{descriptor.PTID}";
                }

                string uniqueId = $"{discoveryId}_{descriptor.DiscoveryName}{deduplicatorLabel}";
                string discoveryTopic = $"homeassistant/{type}/{uniqueId}/config";
                MqttMessage message = new MqttMessage(discoveryTopic);

                message.AddProperty("unique_id", uniqueId);

                string stateTopic = $"{device.MqttTopic}/{descriptor.DiscoveryName}{deduplicator}{descriptor.DiscoveryTopicSuffix}";
                message.AddProperty("state_topic", stateTopic);

                if (descriptor.IsWritable)
                {
                    string commandTopic = $"{device.MqttTopic}/set/{descriptor.SafeName}{deduplicator}{descriptor.DiscoveryTopicSuffix}";
                    message.AddProperty("command_topic", commandTopic);
                }

                message.AddProperty("name", descriptor.Name);
                message.AddProperty("object_id", $"{discoveryId}_{device.Name}_{descriptor.Name}{deduplicatorLabel}");

                foreach (var (key, value) in descriptor.DiscoveryProperties)
                {
                    message.AddProperty(key, value);
                }

                // Try to guess a suitable icon if none given
                if (!message.Content.ContainsKey("icon"))
                {
                    if (descriptor.Name.ToLower().Contains("brenner"))
                        message.AddProperty("icon", "mdi:fire");
                    else if (descriptor.Name.ToLower().Contains("solar"))
                        message.AddProperty("icon", "mdi:solar-panel");
                    else if (descriptor.Name.ToLower().Contains("ventil"))
                        message.AddProperty("icon", "mdi:pipe-valve");
                    else if (descriptor.Name.ToLower().Contains("heizung"))
                        message.AddProperty("icon", "mdi:radiator");
                    else if (descriptor.Name.ToLower().Contains("pumpe"))
                        message.AddProperty("icon", "mdi:pump");
                    if (descriptor.Name.ToLower().Contains("druck"))
                        message.AddProperty("icon", "mdi:gauge");
                }

                message.AddProperty("device", GetDiscoveryDeviceInfo(discoveryId, device));
                result.Add(message);
            }
            return result;
        }

        private JsonObject GetDiscoveryDeviceInfo(string discoveryId, Ism7Config.Device device)
        {
            return new JsonObject
            {
                { "configuration_url", $"http://{device.Ip}/" },
                { "manufacturer", "Wolf" },
                { "model", device.Name },
                { "name", $"{discoveryId} {device.Name}" },
                {
                    "connections", new JsonArray
                    {
                        new JsonArray
                        {
                            "ip_dev",
                            $"{device.Ip}_{device.Name}"
                        }
                    }
                }
            };
        }
    }
}
