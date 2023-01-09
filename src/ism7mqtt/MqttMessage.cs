using System;
using System.Collections.Generic;
using System.Linq;

namespace ism7mqtt;

public class MqttMessage
{
    private readonly List<string> _parts;

    public MqttMessage(string topic, string content)
        :this(content)
    {
        _parts = new List<string> { topic };
    }

    private MqttMessage(IEnumerable<string> parts, string content)
        : this(content)
    {
        _parts = new List<string>(parts);
    }

    private MqttMessage(string content)
    {
        Content = content;
    }

    public MqttMessage AddPrefix(string prefix)
    {
        _parts.Insert(0, prefix);
        return this;
    }

    public MqttMessage AddSuffix(string suffix)
    {
        _parts.Add(suffix);
        return this;
    }

    public MqttMessage Clone()
    {
        return new MqttMessage(_parts, Content);
    }

    public MqttMessage SetContent(string content)
    {
        return new MqttMessage(_parts, content);
    }

    public string Path => String.Join('/', _parts);

    public string Content { get; }

}