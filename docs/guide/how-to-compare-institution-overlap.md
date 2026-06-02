# Compare institutions with the overlap matrix

This guide shows you how to use the Overlap Matrix page to see how many stock holdings two or more institutional investors share, so you can spot funds that move together or hold a common book.

## Open the overlap matrix

1. Go to `http://localhost:8080` and click **More** in the top navigation, then **Overlap Matrix** (or go directly to `http://localhost:8080/institutions/overlap-matrix`).

2. The page opens with an empty picker prompting you to choose institutions.

## Pick the institutions to compare

1. Type an institution name or CIK number into the **Type an institution name or CIK** box. A CIK (Central Index Key) is the SEC's unique identifier for each filer.

2. Choose a match from the suggestions. It becomes a chip above the search box. Repeat to add more — you can compare between 2 and 10 institutions at once.

3. To remove an institution, click the **×** on its chip.

4. Optionally pick a **Report date** to compare a specific 13F quarter. Leave it on the default to use the most recent quarter the chosen funds share.

5. Click **Compare** to build the matrix.

If you see "Pick at least 2 institutions", add another fund. If you see "No common report dates", the funds you chose never filed 13Fs for the same quarter — swap one out.

## Read the matrix

The page shows two tables.

**Shared Ticker Counts** is the matrix itself. Each row and column is one institution:

- A cell where a row and column meet shows how many stock tickers those two funds both hold. Darker cells mean more overlap.
- A cell on the diagonal (a fund against itself), shaded grey, shows that fund's total number of positions.

**Fund Summary** lists each selected institution with these columns:

| Column | What it shows |
|--------|---------------|
| **Institution** | The fund's name, linking to its full profile. |
| **CIK** | The fund's SEC Central Index Key. |
| **Positions** | How many stock positions the fund reported that quarter. |
| **Total Value ($M)** | The fund's total reported portfolio value, in millions of dollars. |

Click any institution name in either table to open its full profile — see [Browse institutional portfolios and run a backtest](how-to-browse-institutions.md).
