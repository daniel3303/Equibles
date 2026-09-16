# Euronext delayed trades fixture

Source: `https://marketdata.euronext.com/data-reporting-service/trades-file/download/EQUITIES/PREVIOUS_TRADING_DAY/LIS`
Captured: 2026-09-16 03:54 UTC (the session of 2026-09-15), zip entry `Trades_Equities.csv`, 16563 rows.
Terms: `https://www.euronext.com/delayed-data-terms-conditions` (line 1 of the file is the venue's notice, kept verbatim).

`Trades_Equities.lisbon-excerpt.csv` keeps 57 real rows in file order:

- every XLIS print of PTPTC0AM0009 (47 rows, its closing-auction cluster at 15:35:20Z included),
- two lit prints and the three RFPT dark prints of PTEDP0AM0009 (mechanism 3, benchmark RFPT),
- the five ENXL prints of PTRIZ0AM0009 (the LIS file carries more than one venue).

The 12 rows after them are synthetic (identifiers start with `SYNTH-`), because the real file had no
modification, missing-price or foreign-currency rows:

- amendment original
- amendment row, the last word for its id
- cancelled original with an outlier price
- cancellation row
- missing price
- non-monetary notation
- foreign currency
- off-book print on the next local day
- duplicate identifier, first copy
- duplicate identifier, second copy
- scientific quantity
- malformed row (19 columns)

`Trades_Equities.header-only.csv` is the notice and header alone. Tests zip a CSV in memory to mirror the served file.
