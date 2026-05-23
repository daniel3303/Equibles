# Explore a company's data on the web portal

This tutorial walks you through a stock's profile page on the Equibles web portal, showing you where to find prices, institutional holdings, SEC filings, short data, financials, and trading activity from insiders and members of Congress.

You'll need about 10 minutes. The stack should be running and have had at least an hour to sync data — if you just finished the [install tutorial](tutorial-install.md), give the workers some time to populate before starting here.

## Find a stock

1. Open the web portal at `http://localhost:8080`.

2. Click **Stocks** in the top navigation. You'll see a searchable list of all tickers the worker has discovered so far.

3. Type `AAPL` (or any ticker you know is synced) into the search box and click the result. You're now on the stock profile page.

At the top of the profile you'll see the company name, ticker, exchange, and industry. Below that is a row of tabs — each tab shows a different slice of data for this company. Let's walk through them.

## Price History

This is the default tab. It shows a chart of the stock's daily closing price over time, plus a table of historical OHLCV data (open, high, low, close, volume, adjusted close). The data comes from Yahoo Finance and updates daily.

## Institutional Holders

Click **Institutional Holders**. This tab shows every institution that reported holding this stock in their most recent 13F filing with the SEC. You'll see:

- The institution's name (click it to see that institution's full portfolio).
- Number of shares held, dollar value, and what percentage of the company's float they own.
- A date picker to view holdings from previous quarters.
- A **Download CSV** button to export the full holder list.

## Short Volume

Click **Short Volume**. This tab charts daily short-sale volume alongside total volume, so you can see what fraction of trading was short selling on any given day. The data comes from FINRA and requires a [FINRA API key](how-to-set-up-finra-api-key.md) to be configured.

## Short Interest

Click **Short Interest**. This shows the bi-monthly short-interest snapshots — how many shares are sold short in total, the short interest ratio (days to cover), and the percentage of float shorted. This data also comes from FINRA.

## Fails to Deliver

Click **Fails to Deliver**. The SEC publishes data on trades that failed to settle. This tab charts the daily FTD count and dollar value for the stock. Large spikes can indicate settlement stress.

## Financials

Click **Financials**. This tab shows the company's financial facts extracted from XBRL data in their SEC filings — revenue, net income, total assets, earnings per share, and other standard accounting metrics. You can toggle between annual and quarterly views and browse the full list of reported facts.

## SEC Filings

Click **SEC Filings**. Here you'll find every filing the company has made with the SEC — 10-K annual reports, 10-Q quarterly reports, 8-K current reports, proxy statements, and more. Click any filing to read the full document. If you've [enabled semantic search](how-to-enable-embedding-search.md), you can also search across the text of all filings.

## Insider Trades

Click **Insider Trades**. This tab lists stock transactions by company insiders (officers, directors, and large shareholders) as reported on SEC Form 4. You'll see who traded, when, how many shares, at what price, and whether it was a buy or a sell.

## Congressional Trades

Click **Congressional Trades**. Members of the U.S. House of Representatives are required to disclose their stock trades. This tab shows any reported purchases or sales of this stock by members of Congress, including the estimated transaction amount and the disclosure date.

## You're done

You've seen every data dimension available on a stock's profile page. From here you can:

- Browse other stocks using the **Stocks** navigation.
- Check who's buying and selling across the whole market on the [Holdings Activity](http://localhost:8080/holdings/activity) page.
- Screen for stocks by institutional ownership using the [Holdings Screener](how-to-use-holdings-screener.md).
- Ask your AI assistant deeper questions about any of this data — it has access to the same information through MCP tools.
