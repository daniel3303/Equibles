# Browse investment advisers (SEC Form ADV)

This guide shows you how to use the Investment Advisers page to look up SEC-registered investment advisers and read their Form ADV profiles — assets under management, location, employee count, and fee structure.

## Open the advisers page

1. Go to `http://localhost:8080` and click **More** in the top navigation, then **Investment Advisers** (or go directly to `http://localhost:8080/advisers`).

2. The page opens on the largest advisers by regulatory assets under management. If the list is empty, the worker has not finished importing Form ADV data yet — check back after the initial sync.

## Find an adviser

1. Type a firm name into the **Search advisers by name** box at the top and submit.

2. The list filters to matching advisers. Clear the box and submit again to return to the full ranking by assets under management.

The list shows 50 advisers per page, with these columns:

| Column | What it shows |
|--------|---------------|
| **Adviser** | The firm's legal name, with its CRD number underneath. |
| **Location** | The firm's main office location. |
| **Regulatory AUM** | Total regulatory assets under management reported on Form ADV. |
| **Employees** | Number of employees the firm reports. |

## Read an adviser profile

1. Click any adviser's name to open its profile (or go directly to `http://localhost:8080/advisers/<crd>`, using the firm's CRD number).

2. The top of the profile shows three assets-under-management figures:
   - **Total regulatory AUM** — all assets the firm manages.
   - **Discretionary** — assets the firm can trade without per-trade client approval.
   - **Non-discretionary** — assets where the client approves each trade.

3. The **Firm details** card lists the firm's CRD number, SEC file number, main office, employee count, SEC registration status, website (when reported), and fee structure (for example, a percentage of assets under management). A line at the bottom shows the Form ADV report date the figures are drawn from.

## What is a CRD number?

A CRD (Central Registration Depository) number is the unique identifier the SEC and FINRA assign to each registered firm. Equibles uses it in the profile URL (`/advisers/<crd>`) and shows it under each firm's name so you can tell apart firms with similar names.
