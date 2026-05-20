# Back up and restore your database

Snapshot the Equibles database to a single compressed file you can copy off-machine, then restore from that file on demand. Everything Equibles knows about your data lives in the `db` container's `db-data` volume — back that up and you're covered.

## Back up

1. While the stack is running, dump the database to a gzipped SQL file:

   ```bash
   docker compose exec -T db pg_dump -U postgres equibles \
     | gzip > equibles-$(date +%F).sql.gz
   ```

   The `-T` tells Docker not to allocate a TTY (otherwise `pg_dump` sees a terminal and may produce garbled output). The shell substitution names the file after today's date — e.g. `equibles-2026-05-20.sql.gz`.

2. Wait for the command to finish. On a large database this can take several minutes; you'll get the shell prompt back when it's done. Check the file size — anything from a few MB (fresh install) to several GB (years of SEC filings) is normal.

3. Copy the file somewhere safe. Off the host machine is best — an S3 bucket, a NAS, a cloud drive, your laptop's backups. Treat it like any other database backup; whoever can read it can read your data.

## Restore

The restore wipes the existing database and replaces it with the contents of your snapshot. Read the steps before running them.

1. Make sure the stack is running but the worker isn't writing to the database during the restore. Stop `worker` (and `mcp`, which uses the DB) but keep `db` up:

   ```bash
   docker compose stop worker mcp web
   ```

   The `db` container stays up because `psql` needs to connect to it.

2. Drop and recreate the database. This is the destructive step — if you have a current backup, great; if not, take one first with the **Back up** section above:

   ```bash
   docker compose exec -T db psql -U postgres -c \
     'DROP DATABASE IF EXISTS equibles WITH (FORCE); CREATE DATABASE equibles;'
   ```

3. Load the snapshot into the fresh database:

   ```bash
   gunzip -c equibles-2026-05-20.sql.gz \
     | docker compose exec -T db psql -U postgres equibles
   ```

   Replace the filename with the snapshot you want to restore. The command can take as long as the original dump did, sometimes longer if there are large indexes to rebuild.

4. Start the rest of the stack:

   ```bash
   docker compose up -d web mcp worker
   ```

5. Open `http://localhost:8080/status` — the row counts should match the snapshot's. Worker error logs may show one or two transient errors from scrapers catching up; those clear in the next cycle.

## What you don't need to back up

- The `web-keys` Docker volume. Losing it just invalidates existing auth cookies — users sign in again on the next visit.
- The `ollama-data` Docker volume (only present if you enabled the embedding profile). The model is regenerable; `docker compose --profile embedding up -d` will re-download it.
- The container images themselves. `docker compose up -d --build` rebuilds them from the source you have in `git`.

If you want a complete machine-level backup instead of an application-level one, snapshot the entire `db-data` Docker volume directly (`docker run --rm -v equibles_db-data:/data -v $(pwd):/backup alpine tar czf /backup/db-data.tgz -C /data .`). That captures Postgres's on-disk format including config files, but the file is larger and only restorable into the same Postgres version.
