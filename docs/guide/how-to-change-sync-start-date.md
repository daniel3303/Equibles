# Change how far back data syncs

This how-to shows you how to change the earliest date scrapers fetch from on a fresh install. Use this to pull more history (back to 2000) for deeper analysis, or to skip older data for a faster first run.

You need to set this **before** the first `docker compose up` (or before any data has been written). Once a scraper has stored a row, it resumes from `max(date) + 1` and ignores this setting.

## Steps

1. **Open `.env`** in the project root. If the file doesn't exist yet, copy it from the template:

    ```bash
    cp .env.example .env
    ```

2. **Add or edit** the `Worker__MinSyncDate` line. Use ISO date format (`YYYY-MM-DD`):

    ```env
    # .env — start syncing from 2024 instead of the default 2020
    Worker__MinSyncDate=2024-01-01
    ```

    The default is `2020-01-01`. The supported floor is `2000-01-01`.

3. **Start the stack** (or restart it if it was already running):

    ```bash
    docker compose up -d
    ```

4. **Confirm the setting took effect.** Open the [Status page](http://localhost:8080/status) and watch the data counts. The further back the date, the longer the initial backfill — SEC filings and 13F holdings from 2000 can take hours.

## Already running with data?

If scrapers have already stored data, changing `Worker__MinSyncDate` has no effect — they will pick up from `max(date) + 1` per scraper. To re-pull older history you need to drop and reseed the affected tables first (see [Back up and restore your database](how-to-back-up-and-restore.md) for the safe order of operations).
