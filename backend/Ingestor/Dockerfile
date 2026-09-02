FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["Ingestor/Ingestor.csproj", "Ingestor/"]
COPY ["Core/Core.csproj", "Core/"]
COPY ["Data/Data.csproj", "Data/"]
RUN dotnet restore "Ingestor/Ingestor.csproj"

COPY . .
WORKDIR /src/Ingestor
RUN dotnet publish "Ingestor.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /home/app/crashes && chown app:app /home/app/crashes
USER app

ENV INGESTOR_HEARTBEAT_PATH=/tmp/ingestor-heartbeat
HEALTHCHECK --interval=60s --timeout=5s --start-period=60s --retries=3 \
    CMD test -f "$INGESTOR_HEARTBEAT_PATH" \
        && test "$(($(date +%s) - $(stat -c %Y "$INGESTOR_HEARTBEAT_PATH")))" -lt 300 \
        || exit 1

ENTRYPOINT ["dotnet", "Ingestor.dll"]
