FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/SmartLedger.Api/SmartLedger.Api.csproj src/SmartLedger.Api/
COPY src/SmartLedger.Application/SmartLedger.Application.csproj src/SmartLedger.Application/
COPY src/SmartLedger.Domain/SmartLedger.Domain.csproj src/SmartLedger.Domain/
COPY src/SmartLedger.Infrastructure/SmartLedger.Infrastructure.csproj src/SmartLedger.Infrastructure/
RUN dotnet restore src/SmartLedger.Api/SmartLedger.Api.csproj --runtime linux-x64 -p:PublishReadyToRun=true

COPY src/ src/
RUN dotnet publish src/SmartLedger.Api/SmartLedger.Api.csproj \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained false \
    --no-restore \
    --output /app/publish \
    -p:UseAppHost=false \
    -p:PublishReadyToRun=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "SmartLedger.Api.dll"]
