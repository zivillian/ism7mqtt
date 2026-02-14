# ism7mqtt

Get all statistics and values from your Wolf ISM7 and send them to an mqtt server without using the smartset cloud or scraping the smartset UI. It connects directly to your ism7.

## How?

Create a parameter.json file which is needed for ism7mqtt by running the ism7config tool on any machine which can connect to your ism7. This only needs to be done once (or after you changed your your Wolf setup):

```sh
ism7config -i <ism7 ip/host> -p <ism7 password>
```

Run ism7mqtt on any machine which can connect to your ism7 and an mqtt server.

```sh
ism7mqtt -m <mqttserver> -i <ism7 ip/host> -p <ism7 password>
```

Do not forget to put the generated parameter.json next to ism7mqtt or specify the path with `-t .../parameter.json`.

### Docker

If you want to run this via docker, use:

```sh
docker run -d --restart=unless-stopped -v ./parameter.json:/app/parameter.json -e ISM7_MQTTHOST=<mqttserver> -e ISM7_IP=<ism7 ip/host> -e ISM7_PASSWORD=<ism7 password> zivillian/ism7mqtt:latest
```

### HomeAssistant

There is a HomeAssistant integration available at [b3nn0/hassio-addon-ism7mqtt](https://github.com/b3nn0/hassio-addon-ism7mqtt).

## Firmware < 200

The old firmware (Software < 200) uses a different port (9091). ism7config tries to automatically detect and save the port in the parameter.json.

## Cons

The ism7 accepts only a single connection, so you cannot use the Smartset application or mobile app while ism7mqtt is running.

## Configuration

The parameter.json contains all devices and the corresponding properties for the installation extracted from your setup. You can remove any property which is not needed.

## MQTT

ism7mqtt initially fetches all properties declared in parameter.json and afterwards subscribes to changes with an intervall of 60 seconds. Whenever new values are received from ism7 a json update with all those properties is published to mqtt. Please be aware that an update contains only the changed properties - so only the initial message may contain all properties.

You can also enable separate topics (`--separate`), which will report each value in its own nested topic, and disable the json output (`--disable.json`) if you don't need it.

Each device on the bus (and present in the parameter.json) is reported via its own topic. The format is

```txt
Wolf/<ism7 IP address without dots>/<device type>_<device bus address>
```

For each property of type ListParameter (basically all comboboxes) nested values are reported - the original numerical value (`value`) and the german text representation (`text`).

Duplicate properties are reported as a sub property or topic with their numerical identifier (property id) to make them unique.

## Writing MQTT
Sending MQTT messages in order to set values involves using a general structure to publish messages.

> [!NOTE]
> Please be aware that not all properties are writable. The ism7mqtt software attempts to validate whether a property can be set, but this may not always be accurate.

Use this topic to send **JSON data**:
```txt
Wolf/<ism7 IP address without dots>/<device type>_<device bus address>/set
```

Use this topic to send **single values**:
```txt
Wolf/<ism7 ip address>/<device type>_<device bus address>/set/<property name>/...
```

### Example
Goal: Switching heating mode on or off.


Observing the MQTT output published via topic `Wolf/192168127/MK_BM-2_0x85` reveals the following structure:

```json
{
    "Programmwahl": {
        "360051": {
            "value": 0,
            "text": "Standby"
        },
        "360058": 0
    }
}
```
You can now pick either property **value** or **text** to set a new heating mode. To do so you publish the following JSON payload to topic ```Wolf/192168127/MK_BM-2_0x85/set```

```json
{
    "Programmwahl": {
        "360051": {
            "value": 0
        }
    }
}
```

or

```json
{
    "Programmwahl": {
        "360051": {
            "text": "Standby"
        }
    }
}
```


If done correctly ism7mqtt should greet you with the following messages (```-d``` debug enables additional xml output):
```txt
received mqtt with topic 'Wolf/192168127/MK_BM-2_0x85/set' '{"Programmwahl":{"360051":{"text":"Auto"}}}'
> <?xml version="1.0" encoding="utf-16"?><tbreq xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" bn="11" gw="1" ae="true" ty="write"><iwr se="" ba="0x85" in="10100" dl="0x01" dh="0x00" /></tbreq>
< ?<?xml version="1.0" encoding="utf-8"?><tbres bn="11" gw="" st="OK" ts="2024-04-19T22:09:22" emsg=""><iac se="0" ba="0x85" in="10100" dl="0x1" dh="0x0" st="OK"/></tbres>
publishing mqtt with topic 'Wolf/192168127/MK_BM-2_0x85' '{"Programmwahl":{"360051":{"value":1,"text":"Auto"},"360058":1}}'
```

### Publishing Single Values
For single value updates instead of JSON, publish directly to the corresponding MQTT topic.

Append each part of a nested JSON property to the topic path:
```txt
Wolf/<ism7 IP address without dots>/<device type>_<device bus address>/set/<property name>/...
```
Example topics to update single properties:
```txt
Wolf/<ism7 IP address without dots>/WWSystem_BM-2_0x35/set/Programmwahl/35012/value
Wolf/<ism7 IP address without dots>/WWSystem_BM-2_0x35/set/Programmwahl/35012/text
```

## Bugs / Missing Features

If something is not working in your setup, you can get more output by using the debug switch `-d`. This will dump the communication with the ism7 (including your password). Please include a redacted version of this dump when opening an issue and also attach your parameter.json.

## Protocol

See [PROTOCOL.md](PROTOCOL.md)
