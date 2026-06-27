FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["Api/Api.csproj", "Api/"]
COPY ["Core/Core.csproj", "Core/"]
COPY ["Data/Data.csproj", "Data/"]
RUN dotnet restore "Api/Api.csproj"

COPY . .
WORKDIR /src/Api
RUN dotnet publish "Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
# CrashReporter writes durable crash files + sentinels here. Create the directory
# owned by the non-root app user (while still root) so a named Docker volume mounted
# at this path inherits app ownership — a root-owned mount point would make the file
# sink silently fail (writes denied) and defeat the "don't lose crash info" goal.
RUN mkdir -p /home/app/crashes && chown app:app /home/app/crashes
# Drop privileges to the non-root user provided by the .NET base image
# (uid 1654, see Microsoft's documentation). Container escape via the API
# process then no longer grants root on the host.
USER app

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl --fail --silent http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "Api.dll"]
