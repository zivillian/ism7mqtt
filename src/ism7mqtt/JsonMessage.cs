using System;
using System.Text.Json.Nodes;

namespace ism7mqtt
{
    public class JsonMessage
    {
        public JsonMessage(string path, JsonObject content)
        {
            Path = path;
            Content = content;
        }

        public string Path { get; }

        public JsonObject Content { get; }
    }
}