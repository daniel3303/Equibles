# Change the log level for troubleshooting

This guide shows you how to increase or decrease the amount of detail Equibles writes to its logs, which is useful when diagnosing scraper errors or unexpected behavior.

## Available log levels

Equibles uses Serilog, which defines six levels from most to least verbose:

| Level | What it includes |
|-------|-----------------|
| `Verbose` | Everything, including very low-level framework internals. Extremely noisy. |
| `Debug` | Detailed diagnostic events. Useful for tracing a specific scraper run. |
| `Information` | Normal operational events (scraper started, batch imported, etc.). |
| `Warning` | Unexpected situations that don't stop processing (retries, skipped records). **This is the default.** |
| `Error` | Failures that prevent a single operation from completing. |
| `Fatal` | Failures that crash the entire service. |

Each level includes itself and all levels below it in the table. Setting `Information` means you see `Information`, `Warning`, `Error`, and `Fatal` messages but not `Debug` or `Verbose`.

## Change the level

1. Open your `.env` file in the project root.

2. Find the `MINIMUM_LOG_LEVEL` line and set it to the level you want:

   ```dotenv
   MINIMUM_LOG_LEVEL=Information
   ```

3. Restart the stack so every container picks up the change:

   ```bash
   docker compose restart
   ```

   The setting applies to all containers — `web`, `mcp`, `worker`, and (if running) the embedding worker.

4. Follow the logs in real time to see the new output:

   ```bash
   docker compose logs -f worker
   ```

   Replace `worker` with `web`, `mcp`, or the name of whichever service you're investigating.

## Tips

- Start with `Information` when something seems wrong. It shows what each scraper is doing without overwhelming detail.
- Drop to `Debug` only when `Information` doesn't reveal the problem — it produces a lot of output.
- Avoid leaving `Verbose` or `Debug` on in production. The extra I/O slows the stack and fills disk quickly.
- Once you've found the issue, set the level back to `Warning` and restart again.
- For a quick look at recent errors without changing the log level, see [Check worker health and recent errors](how-to-view-status-and-errors.md).
