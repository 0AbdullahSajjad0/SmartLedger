# Build context is the backend/ folder:
#   docker build -t smartledger-api backend

# ---- Build stage: full SDK, compiles and publishes the API ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the files restore needs first, so the restore layer stays cached
# until a project file or package version changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/SmartLedger.Api/SmartLedger.Api.csproj src/SmartLedger.Api/
COPY src/SmartLedger.Application/SmartLedger.Application.csproj src/SmartLedger.Application/
COPY src/SmartLedger.Domain/SmartLedger.Domain.csproj src/SmartLedger.Domain/
COPY src/SmartLedger.Infrastructure/SmartLedger.Infrastructure.csproj src/SmartLedger.Infrastructure/
# For linux-x64 only (what Azure Container Apps runs): the image libraries ship native code for every platform,
# and naming the runtime keeps just this one's.
RUN dotnet restore src/SmartLedger.Api/SmartLedger.Api.csproj --runtime linux-x64

COPY src/ src/
RUN dotnet publish src/SmartLedger.Api/SmartLedger.Api.csproj \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained false \
    --no-restore \
    --output /app/publish \
    -p:UseAppHost=false

# ---- Runtime stage: small chiseled image, no shell, runs as a non-root user ----
# "-extra" includes ICU and time zone data, which SQL Server client and date handling need.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# .NET images listen on 8080 by default (ASPNETCORE_HTTP_PORTS).
EXPOSE 8080
ENTRYPOINT ["dotnet", "SmartLedger.Api.dll"]
