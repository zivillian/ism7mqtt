FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env
WORKDIR /app

COPY src/ism7ssl/ ./ism7ssl/
COPY src/ism7mqtt/ ./ism7mqtt/
COPY src/ism7config/ ./ism7config/
ARG TARGETARCH
RUN if [ "$TARGETARCH" = "amd64" ]; then \
    RID=linux-musl-x64 ; \
    elif [ "$TARGETARCH" = "arm64" ]; then \
    RID=linux-musl-arm64 ; \
    elif [ "$TARGETARCH" = "arm" ]; then \
    RID=linux-musl-arm ; \
    fi \
    && dotnet publish -c Release -o out -r $RID --sc -nowarn:IL2026,IL2104 ism7mqtt/ism7mqtt.csproj \
    && dotnet publish -c Release -o out -r $RID --sc -nowarn:IL2026,IL2104 ism7config/ism7config.csproj

FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-alpine
WORKDIR /app
COPY --from=build-env /app/out .
ENV \
    # https://github.com/dotnet/announcements/issues/20
    # ism7mqtt is only using the invariant culture, but the
    # initializer of a smartset converter creates a german
    # culture (which is not used but fails if only invariant
    # is available)
    DOTNET_SYSTEM_GLOBALIZATION_PREDEFINED_CULTURES_ONLY=false \
    ISM7_DEBUG=false \
    ISM7_MQTTHOST= \
    ISM7_IP= \
    ISM7_PASSWORD= \
    ISM7_MQTTUSERNAME= \
    ISM7_MQTTPASSWORD= \
    ISM7_MQTTQOS= \
    ISM7_DISABLEJSON=false \
    ISM7_RETAIN=false \
    ISM7_INTERVAL=60 \
    ISM7_HOMEASSISTANT_ID=

ENTRYPOINT ["/app/ism7mqtt"]