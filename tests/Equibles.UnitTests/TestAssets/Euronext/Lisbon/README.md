# Lisbon directory captures

- Captured on 2026-09-12 from https://live.euronext.com/en/markets/lisbon/equities/list?page=0.
- `directory.html` is the unchanged directory page; its Drupal settings provide the data gateway and column order.
- `equities.json` is the unchanged POST response from `/en/product_directory/data/stocks-lisbon?mics=ALXL%2CENXL%2CXLIS`.
- Form: `iDisplayStart=0`, `iDisplayLength=100`, `sSortDir_0=asc`, `sSortField=name`, `args[display_datapoints]=name,isin,symbol,market,lastPrice,precentDayChange,lastTradeTime`.
- The response states 49 total/displayed records and contains 49 unique ISIN/MIC pairs: 33 XLIS, 15 ENXL, and 1 ALXL.
- GET pagination arguments are ignored by this source; tests require POST form pagination.
- `altri.html` is the unchanged product page captured on 2026-09-12 from `https://live.euronext.com/en/product/equities/PTALT0AE0002-XLIS`.
- Its `custom.instrument` record identifies issuer code `115374`; unrelated global settings contain another security and must not establish this product's identity.
