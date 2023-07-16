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

The old firmware (Software < 200) uses a different port and SSL3 with cipher TLS_RSA_WITH_RC4_128_MD5 which is unsupported on all modern OS. You can use `oldism7proxy` as a proxy between ism7mqtt and your ism.

## Cons

The ism7 accepts only a single connection, so you cannot use the Smartset application or mobile app while ism7mqtt is running.

## Configuration

The parameter.json contains all devices and the corresponding properties for the installation extracted from your setup. You can remove any property which is not needed.

## MQTT

ism7mqtt initially fetches all properties declared in parameter.json and afterwards subscribes to changes with an intervall of 60 seconds. Whenever new values are received from ism7 a json update with all those properties is published to mqtt. Please be aware that an update contains only the changed properties - so only the initial message may contain all properties.

You can also enable separate topics (`--separate`), which will report each value in its own nested topic, and disable the json output (`--disable.json`) if you don't need it.

Each device on the bus (and present in the parameter.json) is reported via its own topic. The format is

```txt
Wolf/<ism7 ip address>/<device type>_<device bus address>
```

For each property of type ListParameter (basically all comboboxes) nested values are reported - the original numerical value (`value`) and the german text representation (`text`).

Duplicate properties are reported as a sub property or topic with their numerical identifier (property id) to make them unique.

## Writing

You can send values to ism7 via mqtt by publishing json to the topic

```txt
Wolf/<ism7 ip address>/<device type>_<device bus address>/set
```

The payload has the same structure as the published json - you can even set multiple values in one request.

You can also publish single values to the corresponding MQTT topic:

```txt
Wolf/<ism7 ip address>/<device type>_<device bus address>/set/<property name>/...
```

If the property is nested, just append the parts to the end.

So for a duplicate ListParameter publish either json to `Wolf/<ism7 ip address>/WWSystem_BM-2_0x35/set`

```json
{
    "Programmwahl": {
        "35012":{
            "value":1
        }
    }
}
```

or

```json
{
    "Programmwahl": {
        "35012":{
            "text":"Auto"
        }
    }
}
```

or the values to the topics

```txt
Wolf/<ism7 ip address>/WWSystem_BM-2_0x35/set/Programmwahl/35012/value
or
Wolf/<ism7 ip address>/WWSystem_BM-2_0x35/set/Programmwahl/35012/text
```

Please be aware that not all properties can be set - ism7mqtt tries to validate if a property is writable, but this may be incorrect.

## Bugs / Missing Features

If something is not working in your setup, you can get more output by using the debug switch `-d`. This will dump the communication with the ism7 (including your password). Please include a redacted version of this dump when opening an issue and also attach your parameter.json.

## Protocol

See [PROTOCOL.md](PROTOCOL.md)
