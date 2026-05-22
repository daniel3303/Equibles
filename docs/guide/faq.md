# Frequently asked questions

Short answers to recurring questions about running Equibles. For step-by-step instructions, see the [how-to guides](README.md#how-to-guides).

## How do I disable the "update available" banner on the web portal?

The web portal checks GitHub Releases on a schedule and shows a banner when a newer version is published. To turn the check off, set `CHECK_FOR_UPDATES=false` in your `.env` file (or environment) and restart the `web` service. The banner stays hidden until you flip the setting back to `true`. To actually upgrade when an update is available, see [Upgrade to the latest release](how-to-upgrade.md).

## How much disk space does Equibles need?

Plan for about 5 GB to start, growing over time as scrapers backfill more history. The database (held in the `db-data` Docker volume) is by far the largest consumer; the cached SEC filings are smaller. Pulling the full default range (2020 onwards) for every U.S. ticker is the baseline — restricting to a [chosen list of tickers](how-to-restrict-ticker-sync.md) or [a later sync start date](how-to-change-sync-start-date.md) keeps it much smaller, while extending back to 2000 makes it substantially larger. Enabling the [embedding profile](how-to-enable-embedding-search.md) adds roughly 3 GB more (~2 GB Ollama image, ~1.2 GB BGE-M3 model, plus per-chunk vectors).

## How do I wipe the database and start over?

Run `docker compose down -v` from the project root. The `-v` flag deletes the `db-data` Docker volume along with the containers, so the next `docker compose up` starts with an empty database and the scrapers backfill from scratch using whatever `Worker__MinSyncDate` and `Worker__TickersToSync` are currently set in `.env`. This is destructive — if you want to keep a copy of the current data first, take a snapshot using [Back up and restore your database](how-to-back-up-and-restore.md) before running the command.
