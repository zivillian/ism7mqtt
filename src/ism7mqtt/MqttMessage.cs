using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ism7mqtt
{
    public class MqttMessage
    {
        public MqttMessage(string path)
        {
            Path = path;
            Content = new JsonObject();
        }

        public string Path { get; }

        public JsonObject Content { get; }

        public bool HasContent { get; private set; }

        public void AddProperty(string key, JsonNode value)
        {
            AddProperty(new KeyValuePair<string, JsonNode>(key, value));
        }

        public void AddProperty(KeyValuePair<string,JsonNode> property)
        {
            HasContent = true;
            if (Content.TryGetPropertyValue(property.Key, out var value))
            {
                foreach (var prop in property.Value.AsObject())
                {
                    value.AsObject().Add(prop.Key, prop.Value.Deserialize<JsonNode>());
                }
            }
            else
            {
                Content.Add(property);
            }
        }
    }
}