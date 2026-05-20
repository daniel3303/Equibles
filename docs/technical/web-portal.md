# Web Portal

ASP.NET Core MVC portal at [`src/Equibles.Web`](../../src/Equibles.Web). DaisyUI v5 + Tailwind v4 styling, Vite bundling, Razor partials, server-side rendering only — no SPA framework.

## Stock detail — the canonical tab pattern

Most user-facing data lives under `~/Stocks/{ticker}/{tab}`. The tab pattern is the most copy-pasted shape in the codebase; every per-stock view follows it.

The trio:

- **Controller action** in [`StocksController`](../../src/Equibles.Web/Controllers/StocksController.cs) — one action per tab (`Price`, `Holdings`, `ShortVolume`, `ShortInterest`, `Ftd`, `Documents`, `InsiderTrading`, `CongressionalTrades`, `Financials`). Resolves the ticker, calls the matching `StockTabService.LoadXxxTab(stock, ...)`, stashes the result in `ViewData["TabViewModel"]`, returns the shared `Show.cshtml` view.
- **Service method** on [`StockTabService`](../../src/Equibles.Web/Services/StockTabService.cs) — `Task<XxxTabViewModel> LoadXxxTab(CommonStock stock, …)`. The only place that talks to repositories for tab data. All query composition lives here.
- **Razor partial** under [`Views/Stocks/_XxxTab.cshtml`](../../src/Equibles.Web/Views/Stocks/) — typed `@model XxxTabViewModel`. Renders the tab body; the surrounding chrome (breadcrumb, stock header, tab strip) belongs to `Show.cshtml`.

`Show.cshtml` dispatches by `Model.ActiveTab` and calls the right `Html.PartialAsync("_XxxTab", (XxxTabViewModel)ViewData["TabViewModel"])`. The tab-strip `<a asp-action="Xxx" asp-route-ticker="@stock.Ticker">` links route back to the controller's per-tab action.

Why the indirection through `ViewData` rather than a single `StockDetailViewModel` with every tab populated:

- Loading only the active tab keeps the page fast — a Holdings tab doesn't query insider trades.
- Each tab can have its own `LoadXxxTab(...)` signature (e.g. `Holdings` takes an optional `DateOnly? date`); a unified model would force every tab to accept every parameter.
- The pattern stays open to new tabs without changing existing ones.

## Other controllers

| Controller | Route | Role |
|---|---|---|
| [`StocksController`](../../src/Equibles.Web/Controllers/StocksController.cs) | `~/Stocks/...` | The tab pattern above + landing list + `ShowDocument` + `ShowHolder` per-stock holder detail. |
| [`ProfilesController`](../../src/Equibles.Web/Controllers/ProfilesController.cs) | `~/Institutions/{cik}`, `~/Institutions/{cik}/Backtest`, `~/Institutions/Compare`, `~/Institutions/Combined`, `~/Insiders/{ownerCik}`, `~/Congress/{id}` | Global (not stock-scoped) profile pages for institutional holders, insider owners, and Congress members; plus a per-filer backtest view, and side-by-side overlap and consensus-portfolio views across multiple 13F filers. |
| [`HoldingsActivityController`](../../src/Equibles.Web/Controllers/HoldingsActivityController.cs) | `~/Holdings/Activity` | Market-wide quarterly leaderboards aggregated across 13F filers for a `ReportDate`: Top Buys, Top Sells, New Positions, Sold-Out Positions. |
| [`EconomicDataController`](../../src/Equibles.Web/Controllers/EconomicDataController.cs) | `~/Economy/...` | FRED series browsing — category landing + per-series charts. |
| [`CftcController`](../../src/Equibles.Web/Controllers/CftcController.cs) | `~/Futures/...` | CFTC positioning per contract / category. |
| [`MarketController`](../../src/Equibles.Web/Controllers/MarketController.cs) | `~/Market/...` | CBOE VIX + put/call ratios. |
| [`SearchController`](../../src/Equibles.Web/Controllers/SearchController.cs) | `~/Search?q=…` | Aggregate search across registered `ISearchProvider`s. |
| [`StatusController`](../../src/Equibles.Web/Controllers/StatusController.cs) | `~/Status` | Worker health, data counts, error log dashboard. |
| [`ChangelogController`](../../src/Equibles.Web/Controllers/ChangelogController.cs) | `~/Changelog` | Renders `CHANGELOG.md`. |
| [`HomeController`](../../src/Equibles.Web/Controllers/HomeController.cs) | `~/` | Landing page. |
| [`AuthController`](../../src/Equibles.Web/Controllers/AuthController.cs) | `~/Auth/...` | Login form used by the env-based auth scheme. |

## Shared shell

- `_ViewStart.cshtml` pins every view to `_Layout.cshtml`. There is no per-area layout split — one shell for everything.
- `_ViewImports.cshtml` declares the project-wide `@using`s + tag-helper imports + the `Equibles.Web` namespace.
- [`FlashMessage`](../../src/Equibles.Web/FlashMessage) — one-shot session messages used across controllers. `services.AddFlashMessage()` registers the writer; the layout reads via injected `IFlashMessage`. Use for redirects after a write (Auth login success, etc.).
- Layout includes [`StatusBadgeFilter`](../../src/Equibles.Web/Filters/StatusBadgeFilter.cs) (counts recent errors and exposes a badge for the Status link) and [`VersionCheckFilter`](../../src/Equibles.Web/Filters/VersionCheckFilter.cs) (compares `AssemblyInformationalVersion` against the latest GitHub release and surfaces an update banner). Both filters are registered globally on `AddControllersWithViews(options => options.Filters.AddService<...>())`.

## Tag helpers

- [`HeroIconTagHelper`](../../src/Equibles.Web/TagHelpers/HeroIconTagHelper.cs) — `<icon name="..." size="6" />` renders inline SVG from the Heroicons set baked into [`HeroIcons.cs`](../../src/Equibles.Web/TagHelpers/HeroIcons.cs). No icon-font dependency.

## Frontend build

- [`package.json`](../../src/Equibles.Web/package.json) — Vite 6, Tailwind v4 (`@tailwindcss/postcss`), DaisyUI v5, plus `chart.js`, `aos`, `typed.js`.
- [`vite.config.js`](../../src/Equibles.Web/vite.config.js) — single entry `src/index.js`, output to `wwwroot/dist/` as `bundle.js` + `main.css`. `emptyOutDir: true` cleans the bundle on each build.
- [`src/css/main.css`](../../src/Equibles.Web/src/css/main.css) — Tailwind v4 CSS-first config: `@import "tailwindcss"`, `@plugin "daisyui"` + `@plugin "@tailwindcss/typography"`, `@source` directives for `Views/` + `TagHelpers/` + `src/js/`, and the custom `equibles` DaisyUI theme defined via `@plugin "daisyui/theme"`.
- `src/Equibles.Web/src/css/` holds the entry CSS imported by `src/Equibles.Web/src/index.js`; `src/Equibles.Web/src/js/` holds chart wrappers and per-page JS modules. The bundle lazy-imports per-page modules so the Holdings tab doesn't pull Chart.js into the Documents tab.
- Dev cycle: `npm run start` (Vite watch) alongside the .NET app — `AddRazorRuntimeCompilation()` (Web/Program.cs:105) makes Razor edits live without rebuild; Vite handles the bundle.
- CI / Docker: `npm ci && npm run build` runs inside the multi-stage Dockerfile so `wwwroot/dist/` is pre-built into the image.

## Authentication

- Scheme name `EnvAuth` ([`Authentication/EnvAuthHandler.cs`](../../src/Equibles.Web/Authentication/EnvAuthHandler.cs)).
- [`AuthSettings`](../../src/Equibles.Web/Authentication) binds `Auth__Username` + `Auth__Password` (Docker env-var form).
- When both are set, `AddAuthorization(options => options.FallbackPolicy = RequireAuthenticatedUser())` gates every controller; absent settings = open access (the default for the OSS self-hosted shape).
- `MapHealthChecks("/healthz").AllowAnonymous()` opts out so Docker Compose health-check still passes when auth is enabled.
- Data-protection keys persist to `/app/keys` (the `web-keys` Docker volume) so anti-forgery / session cookies survive container restarts.

## Status dashboard

`~/Status` (the StatusController) reads from three sources:

- `Equibles.Errors.Repositories.ErrorRepository` — recent worker / MCP errors.
- [`DataCountService`](../../src/Equibles.Web/Services/DataCountService.cs) — row counts per domain entity, computed via lightweight `COUNT(*)` queries.
- [`ChangelogService`](../../src/Equibles.Web/Services/ChangelogService.cs) — parses `CHANGELOG.md` for the version banner.

The Status page is the operator's primary feedback loop — every new scraper that calls `ErrorReporter.Report(...)` shows up there automatically.

## Conventions

- Controllers are thin: resolve inputs → call the tab/profile service → return the view. No EF queries in controllers.
- Tab partials are typed (`@model XxxTabViewModel`); no `dynamic` or `ViewBag` for tab data.
- Empty-state UI lives in the partial (DaisyUI card + an icon + a sentence). Empty data is rendered, not 404'd.
- Tables follow `table table-zebra table-sm` with right-aligned numeric columns; columns hide on mobile via `hidden md:table-cell` / `hidden lg:table-cell`.
- Routes are lowercased by `RouteOptions.LowercaseUrls = true`; new links use `asp-action` / `asp-route-*` rather than hand-built URLs.

## Adding a new stock tab

1. Add `<TabName>TabViewModel` under `Views/Stocks` (or `ViewModels/Stocks/` for shared/cross-tab types).
2. Add `LoadXxxTab(CommonStock stock, ...)` to `StockTabService`. Compose the query with `IQueryable<>` over the repositories your tab needs (e.g. `InstitutionalHoldingRepository` for a Holdings-style tab); project into the view-model in the service, never in the view.
3. Add the controller action to `StocksController`: resolve ticker, set `ViewData["TabViewModel"]`, return `View("Show", stockDetailViewModel)` with the new tab as `ActiveTab`.
4. Create `Views/Stocks/_XxxTab.cshtml` typed `@model XxxTabViewModel`. Match the existing partials' density (table-zebra, hidden md/lg breakpoints, empty-state card).
5. Add the tab link to the tab strip in `Show.cshtml` (`<a asp-action="Xxx" asp-route-ticker="@stock.Ticker" ...>`).
6. Add a `Html.PartialAsync("_XxxTab", ...)` branch to `Show.cshtml`'s `switch (Model.ActiveTab)`.
