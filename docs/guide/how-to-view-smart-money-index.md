# View the Smart Money Index

This guide shows you how to use the Smart Money Index page to see a basket of the highest-conviction stock picks shared by the top-performing funds, and how that basket has performed against the market.

## What the Smart Money Index is

The index is built automatically from 13F filings:

- Funds are ranked by their 3-year alpha (excess return) against SPY.
- Each top fund's latest 13F portfolio "votes" for the stocks it holds.
- Stocks held by enough of those funds are kept, ranked first by how many funds hold them, then by average position weight.
- The kept stocks form an equal-weighted basket whose performance is tracked forward from the construction date (45 days after the freshest filing, so the simulation never uses holdings before they were public).

## Open the page

1. Go to `http://localhost:8080` and click **More** in the top navigation, then **Smart Money Index** (or go directly to `http://localhost:8080/institutions/smart-money-index`).

2. The page builds the index with default settings. If you see a warning instead of results, there is not enough 13F data and fund scoring yet — run the worker until the initial sync and scoring complete, then reload.

## Tune the basket

Three controls at the top let you rebuild the index. Change any value and click **Rebuild**:

| Control | Default | What it does |
|---------|---------|--------------|
| **Top funds** | 20 | How many of the highest-ranked funds get a vote. |
| **Max holdings** | 25 | The largest number of stocks the basket can contain. |
| **Min funds holding** | 3 | The minimum number of top funds that must hold a stock for it to be included. |

Raising **Min funds holding** makes the basket more selective; raising **Max holdings** lets in more names.

## Read the results

1. A line under the heading summarizes the build: how many holdings the basket contains, how many funds fed it, the portfolio quarter used, and the benchmark (SPY).

2. Two summary cards compare the **Smart Money Index** against the **Benchmark (SPY)**, each showing:
   - **Total return** — the full return over the tracked period.
   - **CAGR** — the compound annual growth rate.
   - **Max drawdown** — the largest peak-to-trough drop.

3. A **Cumulative return** chart plots the index against the benchmark, both starting at 100, so you can see how they diverge over time.

4. A **Constituents** table lists the stocks in the basket, with columns for Ticker, Name, **Held by** (how many top funds hold it), **Avg position weight**, and **Index weight**.

For the fund scores and backtesting that feed this index, see [Browse institutional portfolios and run a backtest](how-to-browse-institutions.md).
