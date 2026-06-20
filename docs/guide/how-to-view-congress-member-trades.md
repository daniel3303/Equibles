# View a member of Congress's trading profile

This guide shows you how to open a member of Congress's profile and read their most recent reported stock trades.

## Find a member of Congress

1. Go to `http://localhost:8080` and click the search box in the top navigation (or go directly to `http://localhost:8080/search`).

2. Type the member's name and submit. Matching people appear under the **Congress** category in the results.

3. Click the member's name to open their profile.

There is no separate Congress page in the menu — search is how you reach a member's profile. If no member matches, the worker has not finished importing congressional trade data yet; check back after the initial sync.

## Read the profile

The profile header shows the member's name with a **Member of Congress** label, followed by a **Recent trades** table of up to their 25 most recent reported transactions:

| Column | What it shows |
|--------|---------------|
| **Ticker** | The traded company's ticker. Click it to open the company page. |
| **Date** | The transaction date the member reported. |
| **Asset** | The name of the traded asset, as filed. |
| **Owner** | Who in the household made the trade — the member, a spouse, or a dependent — as disclosed. |
| **Amount (USD)** | The disclosed dollar range of the trade (for example, `1,001 – 15,000`). |

If the member has no reported trades on file, the page shows "No reported trades" instead of the table.

## Why amounts are shown as a range

Under the STOCK Act, members of Congress disclose each transaction in a broad dollar band rather than an exact figure, so Equibles shows the lower and upper bound of the reported band instead of a single number.

## See also

- Looking for the trades on a specific company instead? Open that company's page and check its congressional-trades tab — see [Explore a company's data on the web portal](tutorial-explore-stock.md).
- [Search for stocks, filings, institutions, and more](how-to-search.md)
