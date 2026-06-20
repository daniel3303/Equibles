# Compare institutions side by side

This guide shows you how to line up two to four institutional investors and read their 13F holdings side by side — each fund's share count and portfolio weight for every stock, next to overlap metrics for the group.

## Open the side-by-side comparison

1. Go directly to `http://localhost:8080/institutions/compare`.

2. In the **Institutions** box, type an institution's name or CIK and pick it from the suggestions. Each one becomes a chip. Add between 2 and 4 institutions.

3. Optionally choose a **Report date** to compare a specific 13F quarter. Leave it on the default to use the most recent quarter all the chosen institutions share.

4. Click **Compare**.

If you added fewer than two institutions, the page asks you to pick at least two. If the institutions never filed 13Fs for the same quarter, you'll see "The selected institutions have no common report date" — swap one out.

## Read the overlap summary

Above the table, a summary card reports the report date used and four overlap measures for the group:

| Measure | What it means |
|---------|---------------|
| **Union positions** | The number of distinct stocks held by at least one of the funds. |
| **Shared positions** | The number of stocks every selected fund holds. |
| **Jaccard similarity** | Shared positions divided by union positions, as a percentage — how much the funds overlap by count. |
| **$-weighted overlap** | The dollar-weighted overlap across shared stocks — how much they overlap by size, not just by name. |

## Read the side-by-side table

Each row is one stock from the union of the funds' holdings. After **Ticker** and **Company**, every selected fund gets its own pair of columns:

| Column | What it shows |
|--------|---------------|
| **Shares** | How many shares that fund holds, or **—** if the fund doesn't hold the stock. |
| **% Port.** | What share of that fund's portfolio the position represents. |

The final **Combined value** column totals the position's value across all the selected funds. Reading across a row tells you, at a glance, which funds hold a stock and how big a bet each one is making relative to the others.

## See also

- [Compare institutions with the overlap matrix](how-to-compare-institution-overlap.md) — a pairwise grid of how many holdings each pair shares, for larger groups.
- [View the combined portfolio of several institutions](how-to-view-combined-institution-portfolio.md) — one pooled book ranked by consensus, rather than per-fund columns.
