FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet restore src/Equibles.Mcp.Server/Equibles.Mcp.Server.csproj
RUN dotnet publish src/Equibles.Mcp.Server/Equibles.Mcp.Server.csproj -c Release -o /app/publish --no-restore

FROM base AS final
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Equibles.Mcp.Server.dll"]
