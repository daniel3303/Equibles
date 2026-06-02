# View 13F trend charts

This guide shows you how to use the 13F Trends page to see how institutional investing has shifted over time — total assets under management, how many funds and positions they report, and how their money is spread across market sectors.

## Open the trends page

1. Go to `http://localhost:8080` and click **More** in the top navigation, then **13F Trends** (or go directly to `http://localhost:8080/holdings/13f-trends`).

2. The page draws its charts from each quarter's 13F snapshot. If you see a "No 13F data yet" message, the worker has not ingested a full quarter yet — check back after the initial sync.

## Read the charts

The page shows three charts, each plotting one quarter per point from oldest to newest.

1. **Total Assets Under Management** — the combined dollar value (in billions) of every position reported across all 13F filers. Use it to see whether institutional money is growing or shrinking quarter over quarter.

2. **Filers and Positions** — two lines on a shared timeline: how many institutions filed that quarter and how many total positions they reported. A dual axis keeps both readable even though the counts differ in scale.

3. **Sector Allocation** — a stacked area chart showing how the total reported value splits across market sectors each quarter. Watch the bands grow or shrink to see money rotating between sectors over time.

For the per-quarter buying and selling behind these trends, see [View quarterly holdings activity across the market](how-to-view-holdings-activity.md).
