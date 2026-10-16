FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER root
RUN mkdir -p /home && chown -R $APP_UID /home
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["DWIS.DAQBridge.RheoSense.WebApp/DWIS.DAQBridge.RheoSense.WebApp.csproj", "DWIS.DAQBridge.RheoSense.WebApp/"]
COPY ["DWIS.DAQBridge.RheoSense.Model/DWIS.DAQBridge.RheoSense.Model.csproj", "DWIS.DAQBridge.RheoSense.Model/"]
RUN dotnet restore "DWIS.DAQBridge.RheoSense.WebApp/DWIS.DAQBridge.RheoSense.WebApp.csproj"
COPY . .
WORKDIR /src/DWIS.DAQBridge.RheoSense.WebApp
RUN dotnet publish "DWIS.DAQBridge.RheoSense.WebApp.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
VOLUME /home
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DWIS.DAQBridge.RheoSense.WebApp.dll"]
