using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Fixtures;

/// <summary>
/// Manages a single Chromium browser instance for the functional test collection.
/// Each test creates its own browser context (cookies, storage) via <see cref="NewPageAsync"/>,
/// so per-test state isolation is preserved without paying the browser-launch cost per test.
///
/// Browser binaries are installed lazily on first use — pulled by Microsoft.Playwright's
/// own installer rather than the bundled playwright.ps1 script so contributors without
/// PowerShell can still run the suite locally.
/// </summary>
public class PlaywrightFixture : IAsyncLifetime {
    public IPlaywright Playwright { get; private set; }
    public IBrowser Browser { get; private set; }

    public async Task InitializeAsync() {
        EnsureBrowserBinariesInstalled();
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync() {
        if (Browser != null) await Browser.CloseAsync();
        Playwright?.Dispose();
    }

    public async Task<IPage> NewPageAsync(string baseUrl) {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions {
            BaseURL = baseUrl,
            IgnoreHTTPSErrors = true,
        });
        return await context.NewPageAsync();
    }

    private static void EnsureBrowserBinariesInstalled() {
        // Idempotent: exit code 0 if Chromium is already present.
        // CI workflows usually pre-install via `playwright install chromium` to avoid the
        // first-test latency, but this fallback keeps local runs working with no setup.
        var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium", "--with-deps"]);
        if (exitCode != 0) {
            throw new InvalidOperationException(
                $"Playwright Chromium install returned exit code {exitCode}. " +
                "Run `pwsh tests/Equibles.FunctionalTests/bin/Debug/net10.0/playwright.ps1 install chromium` " +
                "or set PLAYWRIGHT_BROWSERS_PATH to use an existing installation.");
        }
    }
}

[CollectionDefinition(Name)]
public class FunctionalTestCollection : ICollectionFixture<WebAppFixture>, ICollectionFixture<PlaywrightFixture> {
    public const string Name = "Functional";
}
