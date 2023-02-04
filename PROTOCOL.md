# Protocol

The Smartset application connects to the ism7 via TCP on port 9092. There can only be one connection at a time.

## Encryption

The connection is encrypted using TLS (TLS_RSA_WITH_AES_256_CBC_SHA256) and a client certificate singed by "LuCon Root CA (direct)" is required.

## Payload

Each message starts with a six byte header (4 byte length, 2 byte type). The payload is mostly xml.

## Interpretation

The Smartset application contains [3 helpful xml files](src/ism7mqtt/Resources), which define the conversion, dependencies and human readable names for the data. Another source is the smartsetpc database at `%APPDATA%\Roaming\Wolf GmbH\Smartset\App_Data\smartsetpc.sdf`.

## Diving deeper

If you want look at all the nasty details, I recommend taking a look at [dnSpy](https://github.com/dnSpy/dnSpy/). Using this I was able to get all required information (like database password, encryption key for the XML files in the system-config folder, client certificate, etc.) and was able to patch out the certificate verification to use a mitm proxy.

## Proxy

ism7proxy can be used to intercept and dump the communication between smartset and ism7, but you need to patch [LuCon.WebPortal.StandaloneService.dll](https://github.com/zivillian/ism7mqtt/files/10361898/LuCon.WebPortal.StandaloneService.zip) to remove the certificate validation.
