using System;
using System.Collections.Generic;

namespace ism7mqtt
{
    /// <summary>
    /// The values ism7mqtt cannot start without. Each can come from a command line option or from an
    /// environment variable, so a missing value names both.
    /// </summary>
    public static class RequiredOptions
    {
        public static IReadOnlyList<string> Missing(string ip, string mqttHost, string password)
        {
            var missing = new List<string>();
            if (String.IsNullOrWhiteSpace(ip))
                missing.Add("Wolf hostname or IP address (-i/--ipAddress or ISM7_IP)");
            if (String.IsNullOrWhiteSpace(mqttHost))
                missing.Add("MQTT server (-m/--mqttServer or ISM7_MQTTHOST)");
            if (String.IsNullOrWhiteSpace(password))
                missing.Add("Wolf password (-p/--password or ISM7_PASSWORD)");
            return missing;
        }
    }
}
