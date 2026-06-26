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
# CrashReporter writes durable crash files + sentinels here. Create the directory
# owned by the non-root app user (while still root) so a named Docker volume mounted
# at this path inherits app ownership — a root-owned mount point would make the file
# sink silently fail (writes denied).
RUN mkdir -p /home/app/crashes && chown app:app /home/app/crashes
USER app

# Liveness: the Worker rewrites /tmp/ingestor-heartbeat at the top of every
# loop iteration. The healthcheck rejects the container when the heartbeat
# is older than ~10x the default Job:IntervalMinutes — an interval value
# above ~6h means this needs reconfiguring (rare).
ENV INGESTOR_HEARTBEAT_PATH=/tmp/ingestor-heartbeat
HEALTHCHECK --interval=60s --timeout=5s --start-period=30s --retries=3 \
    CMD test -f "$INGESTOR_HEARTBEAT_PATH" \
        && test "$(($(date +%s) - $(stat -c %Y "$INGESTOR_HEARTBEAT_PATH")))" -lt 21600 \
        || exit 1

ENTRYPOINT ["dotnet", "Ingestor.dll"]
