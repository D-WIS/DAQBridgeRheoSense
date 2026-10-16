FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["DWIS.DAQBridge.RheoSense.OPCUASource/DWIS.DAQBridge.RheoSense.OPCUASource.csproj", "DWIS.DAQBridge.RheoSense.OPCUASource/"]
COPY ["DWIS.DAQBridge.RheoSense.Model/DWIS.DAQBridge.RheoSense.Model.csproj", "DWIS.DAQBridge.RheoSense.Model/"]
RUN dotnet restore "./DWIS.DAQBridge.RheoSense.OPCUASource/DWIS.DAQBridge.RheoSense.OPCUASource.csproj"
COPY . .
WORKDIR "/src/DWIS.DAQBridge.RheoSense.OPCUASource"
RUN dotnet build "./DWIS.DAQBridge.RheoSense.OPCUASource.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./DWIS.DAQBridge.RheoSense.OPCUASource.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
VOLUME /home
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DWIS.DAQBridge.RheoSense.OPCUASource.dll"]
