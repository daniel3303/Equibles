# Browse institutional portfolios and run a backtest

This guide shows you how to search for an institutional holder (hedge fund, mutual fund, pension fund, etc.), view their full 13F portfolio, and backtest the performance of cloning their positions.

## Find an institution

1. Click **Institutions** in the top navigation (or go to `http://localhost:8080/institutions`).

2. Type the institution's name into the search box — for example, "Berkshire" or "Vanguard". Results update as you type.

3. Click an institution to open its profile page.

## Explore the portfolio

The institution profile shows:

- **Summary header** — the institution's name, location, CIK number, total AUM (assets under management), number of positions, top-N concentration, and quarter-over-quarter turnover.
- **Holdings table** — every stock in the institution's most recent 13F filing, with ticker, shares, dollar value, percentage of portfolio, and the change since the prior quarter.
- **Date picker** — switch between report quarters to see how the portfolio evolved over time.
- **Activity section** — new positions, increased positions, decreased positions, and sold-out positions for the selected quarter compared to the prior one.

Click any ticker in the holdings table to jump to that stock's profile page.

## Export the portfolio

Click **Download CSV** at the top of the institution profile to export the full holdings table for the selected quarter.

## Backtest the portfolio

1. From the institution profile, click the **Backtest** button in the top-right corner.

2. The backtest page simulates what would have happened if you had cloned the institution's 13F portfolio at each quarterly rebalance date. It shows:

   - A cumulative return chart plotting the cloned portfolio against a benchmark (S&P 500 by default).
   - Quarter-by-quarter returns for both the clone and the benchmark.
   - Summary statistics: total return, annualised return, and maximum drawdown.

3. Use the date-range controls to narrow the simulation window if you want to focus on a specific period.

## Compare institutions

To see how two or more institutions' portfolios overlap, navigate to the compare URL directly:

```
http://localhost:8080/Institutions/Compare?ciks=0001067983&ciks=0001603466
```

Replace the CIK numbers with the ones shown on each institution's profile page. You can compare up to four institutions at once. The comparison view shows union and intersection position counts, overlap percentage, and a side-by-side holdings table highlighting shared positions.
