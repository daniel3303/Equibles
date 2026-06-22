# Ask your AI assistant for short-squeeze candidates

Equibles ranks every stock that reports short interest into a composite short-squeeze score and exposes the leaderboard through the MCP server, so you can ask your AI assistant which stocks look most squeeze-prone right now. This ranking is available to AI assistants only; the web portal shows short interest per stock, not a squeeze leaderboard.

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- The score is built from FINRA short-interest data, which needs a free FINRA API key — see [Add a FINRA API key](how-to-set-up-finra-api-key.md). Without it there is no short-interest data and no squeeze scores.
- Let the worker run after startup so short interest has been imported for the latest settlement date.

## Ask for the leaderboard

Ask your assistant for the top squeeze candidates:

- "Which stocks have the highest short-squeeze scores right now?"
- "Show me the top 10 short-squeeze candidates."
- "List the most squeeze-prone stocks by score."

The assistant calls the `GetShortSqueezeScores` tool and replies with a ranked table, highest score first. It returns 25 stocks by default; ask for fewer or more (up to 200).

## What you should see

A Markdown table headed by the FINRA settlement date the scores are based on. Each row shows the rank, ticker, composite score, short interest as a percent of shares outstanding, days to cover, and the recent short-volume trend.

The score is a peer-relative 0-100 rank: it averages where each stock falls, as a percentile across every stock reporting short interest, on short percent of shares, days to cover, and the change in short share of total volume. A high score means a stock screens high on those factors relative to its peers — a starting point for research, not a prediction. To dig into one stock's underlying short-interest series, ask about that ticker's short interest instead.

If the reply says no scores are available, the most likely reason is that no short-interest data is on file yet — confirm the FINRA API key is set and the worker has run.
