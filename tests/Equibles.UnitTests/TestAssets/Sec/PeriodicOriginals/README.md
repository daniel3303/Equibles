# Periodic original HTML fixtures

- `Ebs2025Annual.html.gz`: unchanged primary HTML from [the 2025 annual report](https://www.sec.gov/Archives/edgar/data/1367644/000136764426000015/ebs-20251231.htm), gzip SHA-256 `0a216a21385ee8b433f7cc1a58055c4ab92d705c68e6ced5e27e111115e22c67`; 2,878,402 uncompressed bytes and 16 referenced images.
- `Ebs2026Quarterly.html.gz`: unchanged primary HTML from [the June 2026 quarterly report](https://www.sec.gov/Archives/edgar/data/1367644/000136764426000082/ebs-20260630.htm), gzip SHA-256 `7b7820f8ce4c3fde020f6dcfc00cb0885ea379411e826b3d9ad441a90afeb7ae`; 1,509,549 uncompressed bytes and one referenced image.
- Tests wrap each recorded primary in a single-document submission and stub image downloads; they never contact EDGAR.
