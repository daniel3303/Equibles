FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

RUN apt-get update && apt-get install -y curl && \
    curl -fsSL https://deb.nodesource.com/setup_20.x | bash - && \
    apt-get install -y nodejs

COPY Directory.Build.props Directory.Packages.props ./
COPY CHANGELOG.md ./
COPY src/ src/

WORKDIR /src/src/Equibles.Web
RUN npm ci
RUN npm run build

WORKDIR /src
RUN dotnet restore src/Equibles.Web/Equibles.Web.csproj
RUN dotnet publish src/Equibles.Web/Equibles.Web.csproj -c Release -o /app/publish --no-restore

FROM base AS final
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
# start-period must outlast the longest startup migration: web runs pending EF
# migrations before Kestrel binds, and a heavy backfill on a populated DB can
# take several minutes. Probe failures during start-period don't count against
# the container, so a long window can't slow a fast start — it only prevents a
# still-migrating upgrade from being marked unhealthy (which aborts worker/mcp).
HEALTHCHECK --interval=5s --timeout=3s --retries=10 --start-period=10m \
    CMD curl --fail http://localhost:8080/healthz || exit 1
ENTRYPOINT ["dotnet", "Equibles.Web.dll"]
