using System.Text.Json.Serialization;

namespace ism7config;

class Config
{
    public ushort TcpPort { get; set; }

    public List<Device> Devices { get; set; }
}

class Device
{
    [JsonIgnore]
    public long Id { get; set; }

    public string ReadBusAddress { get; set; }

    public string WriteBusAddress { get; set; }

    public int DeviceTemplateId { get; set; }

    [JsonIgnore]
    public List<Parameter> Parameters { get; set; }

    [JsonPropertyName("Parameter")]
    public IEnumerable<long> Ids
    {
        get { return Parameters.Select(x => x.ParameterId); }
    }
}

class Parameter
{
    public long ParameterId { get; set; }
}