FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet restore src/Equibles.Worker.Host/Equibles.Worker.Host.csproj
RUN dotnet publish src/Equibles.Worker.Host/Equibles.Worker.Host.csproj -c Release -o /app/publish --no-restore

# Install Playwright Firefox browser binary (used by Senate disclosure scraper to bypass Akamai)
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN dotnet exec \
    --runtimeconfig /app/publish/Equibles.Worker.Host.runtimeconfig.json \
    --depsfile /app/publish/Equibles.Worker.Host.deps.json \
    /app/publish/Microsoft.Playwright.dll install firefox

FROM base AS final
# Install runtime dependencies: Kerberos + Playwright Firefox system libraries
RUN apt-get update && apt-get install -y --no-install-recommends \
    libgssapi-krb5-2 \
    # Playwright Firefox runtime dependencies
    libgtk-3-0 libdbus-glib-1-2 libxt6 libasound2t64 \
    libx11-xcb1 libxcomposite1 libxdamage1 libxrandr2 \
    libpango-1.0-0 libcairo2 libatk1.0-0 libatk-bridge2.0-0 \
    libgbm1 fonts-liberation \
    && rm -rf /var/lib/apt/lists/*

# Copy Playwright Firefox browser from build stage
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
COPY --from=build /ms-playwright /ms-playwright

WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Equibles.Worker.Host.dll"]
