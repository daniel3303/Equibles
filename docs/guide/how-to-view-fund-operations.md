# View a fund's operations (SEC Form N-CEN)

This guide shows you how to see the operational details a mutual fund, ETF, or closed-end fund reports each year to the SEC on Form N-CEN — its registration type, reporting periods, and the service providers that run and support it.

Only registered investment companies file N-CEN. Operating companies (the typical stock) do not, so this tab is empty for them.

## Open the Fund Operations tab

1. Search for a fund by its ticker — for example, an ETF like `SPY` — and open its profile page.

2. Click the **Fund Operations** tab (or go to `http://localhost:8080/stocks/{ticker}/fund-operations`).

3. If the fund has filed N-CEN reports, you see a table of them followed by the service providers from the most recent report. If not, the tab shows a "No Fund Operations Data" message.

## Read the reports table

Each row is one annual N-CEN report, newest first. The columns are:

- **Filed** — when the report was submitted to the SEC.
- **Period End** — the end of the reporting period the report covers.
- **Type** — the investment-company registration type (for example, `N-1A` for an open-end fund or ETF, `N-2` for a closed-end fund).
- **File Number** — the SEC investment-company file number.
- **Amendment** — whether the report is an **Original** filing or an **Amendment**.
- **First Filing** / **Last Filing** — whether the report is the registrant's first or final N-CEN.

## Read the service providers

Below the table, a card lists the service providers named on the most recent report — the firms that operate and support the fund:

- **Role** — the provider's role, such as adviser, custodian, transfer agent, or auditor.
- **Firm** — the provider's name.
- **Country** — the provider's country.
- **Affiliated** — whether the firm is affiliated with the fund.

## Related

- For the fund's portfolio positions, see the **Fund Holdings** tab on the same profile.
- To walk through every tab on a stock profile, see [Explore a company's data on the web portal](tutorial-explore-stock.md).
