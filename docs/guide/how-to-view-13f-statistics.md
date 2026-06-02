# View 13F statistics

This guide shows you how to use the 13F Statistics page to see how much 13F data Equibles holds — the latest quarter's headline numbers and a quarter-by-quarter history.

## Open the statistics page

1. Go to `http://localhost:8080` and click **More** in the top navigation, then **13F Statistics** (or go directly to `http://localhost:8080/holdings/13f-statistics`).

2. The page reads a per-quarter summary that the worker rebuilds on each 13F import. If you see a "No 13F data yet" message, no quarter has finished importing — check back after the initial sync.

## Read the latest-quarter summary

The top of the page shows six cards for the most recent quarter (its date is shown beside the heading):

| Card | What it shows |
|------|---------------|
| **Filers** | How many institutions filed a 13F that quarter. |
| **Filings** | How many 13F filings were submitted (a filer can submit more than one). |
| **Stocks** | How many distinct stocks appear across all filings. |
| **Positions** | The total number of individual stock positions reported. |
| **Avg Pos/Filer** | The average number of positions per filer (positions ÷ filers). |
| **Total AUM** | The combined reported value of all positions, in billions of dollars. |

## Read the quarterly history

Below the cards, the **Quarterly History** table repeats those figures for every quarter on record, newest first, so you can track how each one has changed over time:

| Column | What it shows |
|--------|---------------|
| **Report Date** | The 13F reporting quarter. |
| **Filers** | Institutions that filed that quarter. |
| **Filings** | Filings submitted that quarter. |
| **Stocks** | Distinct stocks held. |
| **Positions** | Total positions reported. |
| **Avg Pos/Filer** | Average positions per filer. |
| **AUM ($B)** | Combined reported value, in billions of dollars. |

For the charted version of these same trends, see [View 13F trend charts](how-to-view-13f-trends.md).
