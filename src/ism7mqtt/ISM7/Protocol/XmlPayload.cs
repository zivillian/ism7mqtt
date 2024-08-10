using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace ism7mqtt.ISM7.Protocol
{
    public abstract class XmlPayload : IPayload
    {
        private static readonly ConcurrentDictionary<Type, XmlSerializer> _serializers = new ConcurrentDictionary<Type, XmlSerializer>();

        public byte[] Serialize()
        {
            using var sw = new Utf8StringWriter();
            var xmlWriter = XmlWriter.Create(sw, new XmlWriterSettings {Indent = false});

            var serializer = _serializers.GetOrAdd(GetType(), x => new XmlSerializer(x));
            serializer.Serialize(xmlWriter, this);
            sw.Flush();
            var xml = sw.ToString();
            return Encoding.UTF8.GetBytes(xml);
        }

        public abstract PayloadType Type { get; }
    }
}