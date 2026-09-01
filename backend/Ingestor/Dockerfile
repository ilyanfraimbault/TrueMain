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

# Liveness: the Worker rewrites /tmp/ingestor-heartbeat every 30 s from a loop that
# runs for its whole lifetime — during a pass and during the wait between passes
# alike (#1229). The threshold is 10 beats, not a multiple of Job:IntervalMinutes:
# the file no longer tracks pass boundaries, so it stays independent of how the job
# is scheduled and a wedged process is caught in minutes instead of six hours.
# Progress (as opposed to liveness) is tracked separately by the process_runs
# heartbeat, which ages a stalled run out to Abandoned.
ENV INGESTOR_HEARTBEAT_PATH=/tmp/ingestor-heartbeat
HEALTHCHECK --interval=60s --timeout=5s --start-period=60s --retries=3 \
    CMD test -f "$INGESTOR_HEARTBEAT_PATH" \
        && test "$(($(date +%s) - $(stat -c %Y "$INGESTOR_HEARTBEAT_PATH")))" -lt 300 \
        || exit 1

ENTRYPOINT ["dotnet", "Ingestor.dll"]
