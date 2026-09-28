FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env
WORKDIR /app

COPY src/ism7ssl/ ./ism7ssl/
COPY src/ism7mqtt/ ./ism7mqtt/
COPY src/ism7config/ ./ism7config/
ARG TARGETARCH
ARG VERSION=0.0.0-unknown
ENV MINVERVERSIONOVERRIDE=${VERSION}
RUN if [ "$TARGETARCH" = "amd64" ]; then \
    RID=linux-musl-x64 ; \
    elif [ "$TARGETARCH" = "arm64" ]; then \
    RID=linux-musl-arm64 ; \
    elif [ "$TARGETARCH" = "arm" ]; then \
    RID=linux-musl-arm ; \
    fi \
    && dotnet publish -c Release -o out -r $RID --sc ism7mqtt/ism7mqtt.csproj \
    && dotnet publish -c Release -o out -r $RID --sc ism7config/ism7config.csproj

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine
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
    ISM7_HOMEASSISTANT_ID= \
    ISM7_HEALTH_FILE=/tmp/ism7mqtt.alive

# ism7mqtt updates $ISM7_HEALTH_FILE every 30 seconds while the MQTT broker is reachable.
# Note that plain Docker only marks the container as unhealthy, it does not restart it.
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD [ -n "$ISM7_HEALTH_FILE" ] && [ -n "$(find "$ISM7_HEALTH_FILE" -mmin -2 2>/dev/null)" ]

ENTRYPOINT ["/app/ism7mqtt"]