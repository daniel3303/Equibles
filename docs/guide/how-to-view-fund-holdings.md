# View a fund's portfolio holdings (SEC Form N-PORT)

This guide shows you how to see the individual securities a mutual fund, ETF, or closed-end fund owns, taken from the fund's most recent Form N-PORT report to the SEC.

Only registered investment companies file N-PORT. Operating companies (the typical stock) do not, so the Fund Holdings tab appears empty for them.

## Open the Fund Holdings tab

1. Search for a fund by its ticker — for example, an ETF like `SPY` or `QQQ` — and open its profile page.

2. Click the **Fund Holdings** tab (or go to `http://localhost:8080/stocks/{ticker}/fund-holdings`).

3. If the fund has filed an N-PORT report, you see a report header followed by its holdings table. If no report is available yet, the tab shows a "No Fund Holdings Data" message.

## Read the report header

The header summarizes the portfolio snapshot the holdings come from:

- **Series name** — the specific fund series the report covers (falls back to the registrant name).
- **Reported** — the report period end date the holdings reflect.
- **Net Assets** and **Total Assets** — the fund's reported net and total assets for the period.
- **Holdings** — how many distinct positions the report lists in total.
- **Filed** — when the fund submitted the report to the SEC.

## Read the holdings table

The table lists the fund's largest positions by value. When a report has more positions than are shown, a line above the table reads "Showing the N largest holdings by value of M total".

Each row shows:

- **Holding** — the security's name.
- **CUSIP** — its CUSIP identifier.
- **Balance** and **Units** — the quantity held and the unit it is measured in (for example, shares or principal amount).
- **Value** — the position's reported U.S.-dollar value.
- **% Net Assets** — the share of the fund's net assets the position represents.
- **Category** — the N-PORT asset category code (for example, `EC` for common equity, `DBT` for debt, `DE` for a derivative).
- **Country** — the investment's country.

## Related

- For a fund's operational details (service providers, fund type), see the **Fund Operations** tab on the same profile.
- To walk through every tab on a stock profile, see [Explore a company's data on the web portal](tutorial-explore-stock.md).
