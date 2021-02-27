using System.Diagnostics;
using Newtonsoft.Json.Linq;

namespace ism7mqtt
{
    public class MqttMessage
    {
        public MqttMessage(string path)
        {
            Path = path;
            Content = new JObject();
        }

        public string Path { get; }

        public JObject Content { get; }

        public bool HasContent { get; private set; }

        public void AddProperty(JProperty property)
        {
            HasContent = true;
            Content.Add(property);
        }
    }
}