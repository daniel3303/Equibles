# View a company's exempt offerings (SEC Form D)

This guide shows you how to see the private capital raises a company has reported to the SEC on Form D under Regulation D — the offering size, how much was sold, the minimum investment, and how many investors took part.

Public operating companies rarely raise money this way, so this tab is empty for most stocks. It is most useful for private issuers and funds.

## Open the Exempt Offerings tab

1. Search for a company by its ticker and open its profile page.

2. Click the **Exempt Offerings** tab (or go to `http://localhost:8080/stocks/{ticker}/exempt-offerings`).

3. If the company has filed Form D notices, you see a table of them, newest first. If not, the tab shows a "No Exempt Offerings" message.

## Read the offerings table

Each row is one Form D notice. The columns are:

- **Filed** — when the notice was submitted to the SEC.
- **Type** — **New** for an original notice, or **Amendment** when it updates an earlier one.
- **Industry** — the issuer's reported industry group.
- **Offering** — the total dollar size of the raise, or **Indefinite** when the issuer reports no fixed cap.
- **Sold** — the dollar amount sold so far.
- **Min. Invest.** — the minimum investment the issuer will accept.
- **Investors** — how many investors have already invested.
- **Exemptions** — the Regulation D exemptions the issuer claims.

## Related

- To walk through every tab on a stock profile, see [Explore a company's data on the web portal](tutorial-explore-stock.md).
