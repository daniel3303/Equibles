# View the Double-Down Report

This guide shows you how to use the Double-Down Report to find positions where an institution sharply increased its stake in a stock from one quarter to the next — a sign of growing conviction.

## Open the report

1. Go to `http://localhost:8080` and click **More** in the top navigation, then **Double-Down Report** (or go directly to `http://localhost:8080/holdings/double-down-report`).

2. The report compares each institution's holdings against the previous quarter, so it needs at least two quarters of 13F data. If you see a "Not enough data" message, the worker is still backfilling — check back after the initial sync.

## Choose the quarter and threshold

1. Use the **Report Date** dropdown to pick which quarter to analyze. The report compares it against the previous quarter. The dropdown also offers a combined view that aggregates the latest filings across institutions.

2. Use the **Min % Increase** box to set how large a share-count jump counts as a "double down". It defaults to 50%, meaning a position only appears if the institution grew its share count by at least 50% versus the prior quarter. Raise it to see only the most aggressive additions; lower it to widen the net.

3. Submit the form to refresh the table.

## Read the table

Positions are ranked by the size of the increase, largest first, 100 per page. Each row shows one institution's increase in one stock:

| Column | What it shows |
|--------|---------------|
| **Institution** | The fund that increased its position. |
| **Stock** | The stock it added to. |
| **Prior Shares** | Shares held in the previous quarter. |
| **Current Shares** | Shares held in the selected quarter. |
| **Change** | The percentage increase in share count between the two quarters. |
| **Prior Value** | The position's reported dollar value in the previous quarter. |
| **Current Value** | The position's reported dollar value in the selected quarter. |

If no rows appear, no position met your threshold for that quarter — lower the **Min % Increase** value and try again.

For the broader buying-and-selling picture across the whole market, see [View quarterly holdings activity across the market](how-to-view-holdings-activity.md).
