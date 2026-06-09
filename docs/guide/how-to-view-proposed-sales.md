# View proposed insider sales (SEC Form 144)

This guide shows you how to see notices of intent to sell restricted or control shares that insiders and affiliates file with the SEC on Form 144.

A Form 144 signals a *planned* sale — it does not confirm the shares were actually sold. Treat it as a heads-up, not a completed transaction.

## Open the Proposed Sales tab

1. Search for a company by its ticker and open its profile page.

2. Click the **Proposed Sales** tab (or go to `http://localhost:8080/stocks/{ticker}/proposed-sales`).

3. If the company has Form 144 notices, you see a table of them, newest first. If not, the tab shows a "No Proposed Sales" message.

## Read the proposed-sales table

Each row is one Form 144 notice. The columns are:

- **Filed** — when the notice was submitted to the SEC.
- **Seller** — the person or entity proposing the sale.
- **Relationship** — the seller's relationship to the company (for example, officer or director).
- **Shares** — the number of shares the seller intends to sell.
- **Market Value** — the aggregate market value of those shares, as reported on the notice.
- **Approx. Sale** — the approximate date of the proposed sale.
- **Broker** — the broker handling the sale.

## Related

- For completed insider transactions (Forms 3, 4, and 5), see the **Insider Trading** tab on the same profile.
- To walk through every tab on a stock profile, see [Explore a company's data on the web portal](tutorial-explore-stock.md).
