# View the latest 13F filings

This guide shows you how to use the Latest 13F Filings page to see institutional 13F filings as Equibles imports them, newest first — handy for spotting brand-new filers and freshly filed portfolios.

## Open the filings feed

1. Go to `http://localhost:8080` and click **More** in the top navigation, then **Latest 13F Filings** (or go directly to `http://localhost:8080/holdings/latest-13f-filings`).

2. The feed lists the most recently filed 13Fs first, 50 per page, with a running count of how many filings have been imported. If you see "No 13F filings have been imported yet", the worker has not finished its first 13F cycle — check back after the initial sync.

## Read the feed

Each row is one filing:

| Column | What it shows |
|--------|---------------|
| **Filer** | The institution's name. A **New** badge marks its first-ever 13F; an **A** badge marks an amended filing (13F-HR/A). |
| **CIK** | The filer's SEC Central Index Key. |
| **Positions** | How many stock positions the filing reports. |
| **Value ($M)** | The filing's total reported value, in millions of dollars. |
| **Report Date** | The quarter the filing covers. |
| **Filed** | The date the filing was submitted to the SEC. |
| **Imported** | When Equibles ingested the filing. |

## Open a filer's profile

Click any row to open that institution's full profile, where you can see its complete portfolio, history, and backtest. See [Browse institutional portfolios and run a backtest](how-to-browse-institutions.md).
