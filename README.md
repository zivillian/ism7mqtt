# ism7mqtt

Get all statistics and values from your Wolf ISM7 and send them to an mqtt server without using the smartset cloud or scraping the smartset UI. It connects directly to your ism7.

## How?

Run ism7parameter on a Windows machine where [Wolf SmartSet](https://www.wolf.eu/download-asset/22335) is installed and was connected to your ism7 at least once. This will create a parameter.json file which is needed for ism7mqtt.

Run ism7mqtt on any machine which can connect to your ism7 and an mqtt server.

```sh
ism7mqtt -m <mqttserver> -i <ism7 ip/host> -p <ism7 password>
```

Do not forget to put the generated parameter.json next to the ism7client or specify the path with `-t .../parameter.json`.

### Docker

If you want to run this via docker, use:

```sh
docker run -d --restart=unless-stopped -v ./parameter.json:/app/parameter.json -e ISM7_MQTTHOST=<mqttserver> -e ISM7_IP=<ism7 ip/host> -e ISM7_PASSWORD=<ism7 password> zivillian/ism7mqtt:latest
```

## Cons

The ism7 accepts only a single connection, so you cannot use the Smartset application while ism7mqtt is running.

## Configuration

The parameter.json contains all devices and the corresponding properties for the installation extracted from smartset. You can remove any property which is not needed.

## MQTT

ism7mqtt initially fetches all properties declared in parameter.json and afterwards subscribes to changes with an intervall of 60 seconds. Whenever new values are received from ism7 a json update with all those properties is published to mqtt. Please be aware that an update contains only the changed properties - so only the initial message may contain all properties.

Each device on the bus (and present in the parameter.json) is reported via its own topic. The format is

```txt
Wolf/<ism7 ip address>/<device type>_<device bus address>
```

For each property of type ListParameter (basically all comboboxes) two values are reported - the original numerical value and the german text representation (with the suffix `_Text`).

Duplicate properties get a numerical suffix (property id) to make them unique.

## Writing

You can send values to ism7 via mqtt by publishing json to the topic

```txt
Wolf/<ism7 ip address>/<device type>_<device bus address>/set
```

or just the raw value to

```txt
Wolf/<ism7 ip address>/<device type>_<device bus address>/set/<property name>
```

Please be aware that not all properties can be set - ism7mqtt tries to validate if a property is writable, but this may be incorrect (#6).

## Bugs / Missing Features

If something is not working in your setup, you can get more output by using the debug switch `-d`. This will dump the communication with the ism7 (including your password). Please include a redacted version of this dump when opening an issue and also attach your smartset database file (%APPDATA%\Roaming\Wolf GmbH\Smartset\App_Data\smartsetpc.sdf).

## Protocol

See [PROTOCOL.md](PROTOCOL.md)
