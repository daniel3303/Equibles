# Xetra all-tradable-instruments captures

- `tradable-instruments-page.html` is the unchanged page captured on 2026-09-15 from `https://www.cashmarket.deutsche-boerse.com/cash-en/trading/Tradable-Instruments-Xetra`; the former `www.xetra.com` address now answers with a redirect stub.
- The page links exactly one `/resource/blob/<id>/<hash>/data/t7-xetr-allTradableInstruments.csv`; the hash rotates with every publication and is read from the page, never composed.
- `t7-xetr-allTradableInstruments.sample.csv` keeps the file's unchanged three-line preamble (market `XETR`, `Date Last Update: 15.09.2026`, the `;`-separated header) and 36 verbatim rows: 30 `Active` `CS` shares, 1 `PendingDeletion` `CS` share, 3 `ETF` and 2 `ETN` rows that the directory adapter must leave out.
- Every kept share is quoted in EUR on XETR; `Primary Market MIC Code` states the home venue (Vienna for most Austrian rows, Frankfurt for Fabasoft, Kontron and Frequentis) and is what the directory gate reads, because FIRDS places a German share's relevant venue on the Frankfurt floor or a regional exchange rather than on Xetra.
