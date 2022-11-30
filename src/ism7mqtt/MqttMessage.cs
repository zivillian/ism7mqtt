using System.Collections.Generic;
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

        public void AddProperty(KeyValuePair<string,JsonNode> property)
        {
            HasContent = true;
            Content.Add(property);
        }
    }
}