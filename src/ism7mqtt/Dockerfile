FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build-env
WORKDIR /App

COPY . ./
RUN dotnet restore
RUN dotnet publish -c Release -o out -r linux-x64 -p:PublishTrimmed=True --sc -nowarn:IL2026,IL2104

FROM mcr.microsoft.com/dotnet/runtime-deps:6.0-alpine
WORKDIR /App
COPY --from=build-env /App/out .
ENTRYPOINT ["/App/ism7mqtt"]