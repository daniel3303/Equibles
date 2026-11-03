# Government contracts fixtures

Recorded 2026-11-03 UTC from the open.canada.ca CKAN datastore, resource `fac950c0-00d5-4ec1-a4d3-9cbebf98a305` ("Contracts over $10,000", Proactive Publication - Contracts), with the field list `OpenCanadaContracts.PageAddress` requests. The vendor filter keeps each answer to one company's reports.

| File | Request | SHA-256 |
|---|---|---|
| `open-canada-contracts-kbl-2026-2027-Q1.json` | `filters={"reporting_period":"2026-2027-Q1","vendor_name":"KBL PROJECTS LTD."}`, `limit=1000`, `sort=_id` | `b61b51d9bf6b4623db18b73ee549f4f89f127f99d247c93682e6cd6fa8be9eba` |
| `open-canada-contracts-kbl-2026-2027-Q2.json` | same, `2026-2027-Q2` | `66f91e020e1e1e967d67802c429a9326ab9c83e8be115caabf01960c2e2cdc2f` |
| `open-canada-rejected.html` | `limit=1` with the bare user agent `Mozilla/5.0`, which the portal's firewall refuses with status 200 | `b8be05ff79de9274ba77bde66aed8cbf18be337b80311acf2213dbb88e36cf40` |
