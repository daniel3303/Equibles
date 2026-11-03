# Government contracts fixtures

Recorded 2026-11-03 UTC from the open.canada.ca CKAN datastore, resource `fac950c0-00d5-4ec1-a4d3-9cbebf98a305` ("Contracts over $10,000", Proactive Publication - Contracts), with the field list `OpenCanadaContracts.PageAddress` requests. The vendor filter keeps each answer to one company's reports.

| File | Request | SHA-256 |
|---|---|---|
| `open-canada-contracts-kbl-2026-2027-Q1.json` | `filters={"reporting_period":"2026-2027-Q1","vendor_name":"KBL PROJECTS LTD."}`, `limit=1000`, `sort=_id` | `b61b51d9bf6b4623db18b73ee549f4f89f127f99d247c93682e6cd6fa8be9eba` |
| `open-canada-contracts-kbl-2026-2027-Q2.json` | same, `2026-2027-Q2` | `66f91e020e1e1e967d67802c429a9326ab9c83e8be115caabf01960c2e2cdc2f` |
| `open-canada-contracts-calian-dnd-2025-2026-Q3.json` | `filters={"reporting_period":"2025-2026-Q3","vendor_name":"Calian Ltd","owner_org":"dnd-mdn"}`, `limit=1000`, `sort=_id` | `9588f9ab2918354a6a99ae225679148b99b30305a7104273f2700b7de5c44da5` |
| `open-canada-contracts-calian-dnd-2025-2026-Q4.json` | same, `2025-2026-Q4` | `dc7f711455e197533ad2dce25b16a09fe3c4551b35fa19becbd2ad191b856b1b` |
| `open-canada-rejected.html` | `limit=1` with the bare user agent `Mozilla/5.0`, which the portal's firewall refuses with status 200 | `b8be05ff79de9274ba77bde66aed8cbf18be337b80311acf2213dbb88e36cf40` |
