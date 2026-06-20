# View the combined portfolio of several institutions

This guide shows you how to pool several institutional investors into one combined portfolio, so you can see which stocks the group holds in common — the consensus picks — and how much they hold together.

## Open the combined portfolio

1. Go directly to `http://localhost:8080/institutions/combined`.

2. In the **Institutions** box, type an institution's name or CIK and pick it from the suggestions. Each one you add appears as a chip. Add between 2 and 25 institutions.

3. Optionally choose a **Report date** to pool a specific 13F quarter. Leave it on the default to use the most recent quarter all the chosen institutions share.

4. Click **Combine**.

If you added fewer than two institutions, the page asks you to pick at least two. If the institutions you chose never filed 13Fs for the same quarter, you'll see "The selected institutions have no common report date" — swap one out for a fund with more overlapping history.

## Read the combined portfolio

A summary strip at the top shows the report date used, how many funds were combined, how many unique stocks the group holds between them, and — when it applies — how many stocks every selected fund holds. Below it, each pooled fund is listed with its position count and total portfolio value.

The main table is the combined book, with a row per stock:

| Column | What it shows |
|--------|---------------|
| **Ticker** | The stock's ticker, linking to its company page. |
| **Company** | The company name. |
| **# Funds** | How many of the selected institutions hold this stock. |
| **Combined Value (USD)** | The total dollar value the group holds across all selected funds. |
| **Avg % of portfolio** | The average weight this stock carries in the funds that hold it. |

Rows are sorted by **# Funds** first — so the stocks the most institutions agree on sit at the top — and then by combined value, so the group's largest shared positions lead within each consensus tier.

## See also

- [Compare institutions with the overlap matrix](how-to-compare-institution-overlap.md) — a pairwise grid of how many holdings each pair shares, rather than one pooled book.
- [View the Smart Money Index](how-to-view-smart-money-index.md) — a ranked, scored consensus across the highest-performing funds.
