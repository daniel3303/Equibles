# View the 13F Conviction Heat Map

This guide shows you how to use the 13F Conviction Heat Map to find the stocks institutions are crowding into — ranked by a conviction score that blends how many funds are buying in, how many are staying, and how widely the stock is held.

## Open the heat map

1. Go to `http://localhost:8080` and click **More** in the top navigation, then **Conviction Heat Map** (or go directly to `http://localhost:8080/holdings/conviction-heat-map`).

2. The heat map compares each quarter against the one before it, so it needs at least two quarters of 13F data. If you see a "Not enough data" message, the worker is still backfilling — check back after the initial sync.

## Choose the quarter

Use the **Report Date** dropdown to pick which quarter to analyze. It compares that quarter against the previous one. The dropdown also offers a combined view that aggregates the latest filings across institutions.

Only stocks held by at least three filers appear, and the page shows the top 25 by conviction score.

## Read the chart

The bubble chart plots each stock by:

- **Number of 13F Filers** (horizontal) — how many institutions hold it.
- **Conviction Score** (vertical) — the blended score described below; higher is stronger.

A line at the top shows the **13F Universe** — the total number of institutions filing that quarter, for context on how widely held each stock is.

## Understand the score

The conviction score combines three components, each shown in the **Score Components** card:

| Component | Meaning |
|-----------|---------|
| **Net Conviction** | New filers minus filers who sold out, as a percentage of current filers. Positive means more funds opened the position than closed it. |
| **Retention** | The share of last quarter's holders that kept the position, as a percentage. High retention means few funds bailed out. |
| **Universe Penetration** | Current filers as a percentage of all 13F filers — how widely held the stock is across the whole institutional universe. |

## Read the top-scorers table

Below the chart, the **Top 25 by Conviction Score** table lists the leaders, with these columns:

| Column | What it shows |
|--------|---------------|
| **Ticker** | The stock's ticker, linking to its profile. |
| **Name** | The company name. |
| **Score** | The overall conviction score (green when very high, red when low). |
| **Net Conv.** | The net-conviction component, with a +/− sign. |
| **Retention** | The retention component. |
| **Univ. %** | The universe-penetration component. |
| **Filers** | How many institutions hold the stock this quarter. |
| **Value (USD)** | The total reported dollar value held across those filers. |

For the buying-and-selling boards that feed this view, see [View quarterly holdings activity across the market](how-to-view-holdings-activity.md).
