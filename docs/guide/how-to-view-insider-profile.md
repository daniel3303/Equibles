# View an insider's trading profile

This guide shows you how to open a corporate insider's profile and read their most recent reported transactions.

## Find an insider

1. Go to `http://localhost:8080` and click the search box in the top navigation (or go directly to `http://localhost:8080/search`).

2. Type the person's name and submit. Matching people appear under the **Insiders** category in the results.

3. Click the insider's name to open their profile.

There is no separate Insiders page in the menu — search is how you reach an individual insider's profile. For the whole market's insider buying and selling instead, see [View market-wide insider trading activity](how-to-view-insider-activity.md).

## Read the profile

The profile header shows the insider's name followed by a summary line: their role (an officer title, board director, or 10% owner), their location, and the SEC CIK number that identifies them. Below it, a **Recent transactions** table lists up to their 25 most recent reported trades (holdings-only reports are left out):

| Column | What it shows |
|--------|---------------|
| **Ticker** | The company the trade is in. Click it to open the company page. |
| **Date** | The transaction date the insider reported. |
| **Security** | The type of security traded, such as common stock or stock options. |
| **Shares** | The number of shares in the transaction. |
| **Price** | The price per share, in US dollars. |

If the insider has no reported transactions on file, the page shows "No reported transactions" instead of the table.

## See also

- [View market-wide insider trading activity](how-to-view-insider-activity.md)
- [Search for stocks, filings, institutions, and more](how-to-search.md)
