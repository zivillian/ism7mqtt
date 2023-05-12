using System;
using System.Collections.Generic;

namespace ism7mqtt.ISM7.Config
{
    public class ConfigRoot
    {
        public List<ConfigDevice> Devices { get; set; }
    }

    public class ConfigDevice
    {
        public string ReadBusAddress { get; set; }

        public int DeviceTemplateId { get; set; }

        public List<int> Parameter { get; set; }
    }
}
