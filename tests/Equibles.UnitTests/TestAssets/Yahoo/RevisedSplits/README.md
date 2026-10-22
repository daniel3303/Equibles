# Yahoo charts that keep a revised split

- Captured without modification on 2026-10-22 from the public chart endpoint.
- `mgn-2025-08-01-to-2026-10-22.json`: <https://query1.finance.yahoo.com/v8/finance/chart/MGN?period1=1754006400&period2=1792713600&interval=1d&events=div%7Csplit>
- MGN executed one 1:30 reverse split on 2026-09-17. The chart still lists the withdrawn 1:40 for 2026-09-08 and
  serves every bar before that date 40 times too high; the zero-volume 2026-09-08 bar repeats the previous close
  on the 1:30 basis.
