FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:6.0 AS build-env
WORKDIR /app

COPY src/ism7mqtt/ ./
ARG TARGETARCH
RUN if [ "$TARGETARCH" = "amd64" ]; then \
    RID=linux-x64 ; \
    elif [ "$TARGETARCH" = "arm64" ]; then \
    RID=linux-arm64 ; \
    elif [ "$TARGETARCH" = "arm" ]; then \
    RID=linux-arm ; \
    fi \
    && dotnet publish -c Release -o out -r $RID -p:PublishTrimmed=True --sc -nowarn:IL2026,IL2104
COPY openssl.cnf ./out/

FROM --platform=amd64 ubuntu/dotnet-deps:6.0-22.04_beta
FROM --platform=arm64 ubuntu/dotnet-deps:6.0-22.04_beta
FROM --platform=arm mcr.microsoft.com/dotnet/runtime-deps:6.0
WORKDIR /app
COPY --from=build-env /app/out .

ENV OPENSSL_CONF=/app/openssl.cnf

ENTRYPOINT ["/app/ism7mqtt"]