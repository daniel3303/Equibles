using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data.Models;
using Equibles.Finra.Data.Models;
using Equibles.Yahoo.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Equibles.TestSupport;

public static class FinancialEquityRetirementProbe
{
    public static async Task Run(DbContext db, DbContext native, string sql, string scenario)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var stock = new CommonStock { Ticker = "RECON", Name = "Original directory owner" };
        db.Add(stock);
        await db.SaveChangesAsync();
        var listing = await db.Set<EquityListing>()
            .SingleAsync(l => l.Security.EquityIssuerId == stock.Id && l.Ticker == "RECON");
        listing.TradingCurrency = "USD";
        await db.SaveChangesAsync();
        var at = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        db.AddRange(
            new EquityDailyStockPrice
            {
                EquityListingId = listing.Id,
                SourceTicker = "RECON",
                Date = new DateOnly(2025, 1, 1),
                Open = 10.1234m,
                High = 12,
                Low = 9,
                Close = 11,
                AdjustedClose = 10,
                Volume = 123456,
                CreationTime = at,
            },
            new LegacyDailyStockPrice
            {
                CommonStockId = stock.Id,
                Date = new DateOnly(2024, 1, 1),
                Open = 1,
                High = 2,
                Low = 1,
                Close = 2,
                AdjustedClose = 1.1234m,
                Volume = 654321,
                CreationTime = at,
            },
            new EquityIssuerCusipAlias
            {
                EquityIssuerId = stock.Id,
                Cusip = "123456789",
                CreationTime = at,
            },
            new StockSplit
            {
                EquityIssuerId = stock.Id,
                EquityListingId = listing.Id,
                PriceSeriesTicker = "RECON",
                EffectiveDate = new DateOnly(2025, 1, 1),
                Numerator = 2,
                Denominator = 1,
                Source = StockSplitSource.Yahoo,
                CreationTime = at,
                PriceAdjustmentAppliedTime = at,
            },
            new CashDividend
            {
                EquityIssuerId = stock.Id,
                EquityListingId = listing.Id,
                Currency = "USD",
                ExDate = new DateOnly(2025, 1, 1),
                AmountPerShare = 1.234567m,
                Source = CashDividendSource.Yahoo,
                CreationTime = at,
                PriceAdjustmentAppliedTime = at,
                PriceAdjustmentAppliedAmountPerShare = 1.234567m,
            },
            new CorporateActionPriceReconciliationCursor
            {
                Name = "Original cursor",
                LastEquityListingId = listing.Id,
                UpdatedAt = at,
            }
        );
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        if (scenario == "preserve-alias")
        {
            db.Add(
                new EquityListingTickerAlias
                {
                    EquityListingId = listing.Id,
                    Ticker = "FORMER",
                    EvidenceSource = "source-stated-test",
                }
            );
            db.Add(
                new DailyShortVolume
                {
                    EquityListingId = listing.Id,
                    ListedTicker = "FORMER",
                    Date = new DateOnly(2025, 1, 1),
                    ShortVolume = 123.456789m,
                    ShortExemptVolume = 2,
                    TotalVolume = 987,
                    Market = "N",
                }
            );
            await db.SaveChangesAsync();
        }
        if (scenario is not ("preserve" or "preserve-alias"))
        {
            await RefusesIncompleteState(db, sql, scenario);
            return;
        }
        var before = await Snapshot(db);
        var archivedDirectory = await db
            .Database.SqlQueryRaw<string>(
                "SELECT to_jsonb(r)::text AS \"Value\" FROM \"CommonStock\" r"
            )
            .SingleAsync();
        await db.Database.ExecuteSqlRawAsync(sql);
        Require(await Snapshot(db) == before, "Native fields changed during retirement");
        Require(
            await db
                .Database.SqlQueryRaw<int>(
                    """
                    SELECT count(*)::integer AS "Value" FROM information_schema.columns
                    WHERE table_schema = 'public' AND column_name IN ('CommonStockId', 'StockId', 'LastCommonStockId', 'LastListedTicker')
                    """
                )
                .SingleAsync() == 0,
            "Retired identity columns remain"
        );
        Require(
            await db
                .Database.SqlQuery<int>(
                    $"""
                    SELECT count(*)::integer AS "Value" FROM "EquityDirectorySourceRecord"
                    WHERE "Source" = 'common-stock-v1' AND "PayloadJson" = {archivedDirectory}::jsonb
                    """
                )
                .SingleAsync() == 1,
            "Original directory payload was lost"
        );
        Require(
            await db
                .Database.SqlQueryRaw<int>(
                    """
                    SELECT count(*)::integer AS "Value" FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
                    WHERE n.nspname = 'public' AND NOT EXISTS (SELECT 1 FROM pg_depend d
                        WHERE d.classid = 'pg_proc'::regclass AND d.objid = p.oid AND d.deptype = 'e')
                    """
                )
                .SingleAsync() == 6,
            "Temporary functions remain or a permanent guard was removed"
        );
        native.Database.SetDbConnection(db.Database.GetDbConnection());
        await native.Database.UseTransactionAsync(transaction.GetDbTransaction());
        db = native;
        var next = EquityIssuerSeed.Create(Ticker: "AFTER");
        db.Add(next);
        await db.SaveChangesAsync();
        await transaction.CreateSavepointAsync("after_retirement");
        var split = await db.Set<StockSplit>().SingleAsync();
        split.EquityIssuerId = next.Id;
        try
        {
            await db.SaveChangesAsync();
            throw new InvalidOperationException("Native ownership guard allowed another issuer");
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException)
        {
            await transaction.RollbackToSavepointAsync("after_retirement");
        }
        db.ChangeTracker.Clear();
        split = await db.Set<StockSplit>().SingleAsync();
        split.Numerator = 3;
        await db.SaveChangesAsync();
        await db.Entry(split).ReloadAsync();
        Require(split.PriceAdjustmentAppliedTime == null, "Native revision guard stopped working");
        var bar = await db.Set<EquityDailyStockPrice>().SingleAsync();
        bar.Close = 11.4321m;
        await db.SaveChangesAsync();
        await db.Entry(bar).ReloadAsync();
        Require(bar.Close == 11.4321m, "Native price writer still depends on retired storage");
    }

    private static async Task RefusesIncompleteState(DbContext db, string sql, string scenario)
    {
        var mutation = scenario switch
        {
            "price-mismatch" => """
                ALTER TABLE "EquityDailyStockPrice" DISABLE TRIGGER USER;
                UPDATE "EquityDailyStockPrice" SET "AdjustedClose" = "AdjustedClose" + 0.0001;
                ALTER TABLE "EquityDailyStockPrice" ENABLE TRIGGER USER;
                """,
            "unmapped-price-column" =>
                "ALTER TABLE \"ListedDailyStockPrice\" ADD COLUMN \"OriginalExtraEvidence\" text DEFAULT 'must survive';",
            "unvalidated-owner" => """
                ALTER TABLE "StockSplit" DROP CONSTRAINT "CK_StockSplit_CanonicalOwnerMirror";
                ALTER TABLE "StockSplit" ADD CONSTRAINT "CK_StockSplit_CanonicalOwnerMirror"
                    CHECK ("EquityIssuerId" IS NOT DISTINCT FROM "CommonStockId") NOT VALID;
                """,
            "unarchived-source" => """
                ALTER TABLE "EquityDirectorySourceRecord" DISABLE TRIGGER USER;
                DELETE FROM "EquityDirectorySourceRecord" WHERE "Source" = 'common-stock-v1';
                ALTER TABLE "EquityDirectorySourceRecord" ENABLE TRIGGER USER;
                """,
            "wrong-sibling-observation" => """
                INSERT INTO "EquityListing" SELECT (jsonb_populate_record(NULL::"EquityListing",
                    to_jsonb(l) || jsonb_build_object('Id', '99999999-0000-0000-0000-000000000001', 'Ticker', 'SIBLING'))).*
                    FROM "EquityListing" l WHERE "Ticker" = 'RECON';
                INSERT INTO "LegacyEquityListing" ("CommonStockId", "ListedTicker", "EquityListingId")
                    SELECT "Id", 'SIBLING', '99999999-0000-0000-0000-000000000001' FROM "CommonStock";
                INSERT INTO "DailyShortVolume" ("Id", "EquityListingId", "ListedTicker", "Date", "ShortVolume",
                    "ShortExemptVolume", "TotalVolume", "Market", "CreationTime")
                    VALUES (gen_random_uuid(), '99999999-0000-0000-0000-000000000001', 'RECON', '2025-01-01', 12, 1, 100, 'N', now());
                """,
            "wrong-price-mapping-owner" => """
                INSERT INTO "CommonStock" SELECT (jsonb_populate_record(NULL::"CommonStock",
                    to_jsonb(s) || jsonb_build_object('Id', '99999999-0000-0000-0000-000000000002', 'Ticker', 'PRICEONLY'))).*
                    FROM "CommonStock" s WHERE "Ticker" = 'RECON';
                INSERT INTO "EquityDailyStockPrice" SELECT (jsonb_populate_record(NULL::"EquityDailyStockPrice",
                    to_jsonb(p) || jsonb_build_object('Id', gen_random_uuid(), 'EquityListingId', l."Id", 'SourceTicker', 'PRICEONLY'))).*
                    FROM "EquityDailyStockPrice" p CROSS JOIN "EquityListing" l WHERE l."Ticker" = 'PRICEONLY';
                ALTER TABLE "LegacyEquityListing" DISABLE TRIGGER equity_legacy_listing_symbol;
                UPDATE "LegacyEquityListing" SET "CommonStockId" = (SELECT "Id" FROM "CommonStock" WHERE "Ticker" = 'RECON')
                    WHERE "ListedTicker" = 'PRICEONLY';
                ALTER TABLE "LegacyEquityListing" ENABLE TRIGGER equity_legacy_listing_symbol;
                ALTER TABLE "ListedDailyStockPrice" DISABLE TRIGGER USER;
                UPDATE "ListedDailyStockPrice" SET "CommonStockId" = (SELECT "Id" FROM "CommonStock" WHERE "Ticker" = 'RECON')
                    WHERE "ListedTicker" = 'PRICEONLY';
                ALTER TABLE "ListedDailyStockPrice" ENABLE TRIGGER USER;
                """,
            "unknown-owner-constraint" =>
                "ALTER TABLE \"StockSplit\" ADD CONSTRAINT \"UnmigratedOwnerRule\" UNIQUE (\"CommonStockId\");",
            "missing-native-owner-key" =>
                "ALTER TABLE \"StockSplit\" DROP CONSTRAINT \"FK_StockSplit_EquityIssuer_EquityIssuerId\";",
            "unmapped-original-issuer" => """
                ALTER TABLE "CommonStock" DISABLE TRIGGER USER;
                INSERT INTO "CommonStock" SELECT (jsonb_populate_record(NULL::"CommonStock",
                    to_jsonb(s) || jsonb_build_object('Id', '99999999-0000-0000-0000-000000000003', 'Ticker', 'UNMAPPED'))).*
                    FROM "CommonStock" s WHERE "Ticker" = 'RECON';
                ALTER TABLE "CommonStock" ENABLE TRIGGER USER;
                SELECT eq_capture_original_directory_record(to_jsonb(s)) FROM "CommonStock" s WHERE "Ticker" = 'UNMAPPED';
                """,
            "unknown-dependent-view" =>
                "CREATE VIEW \"UnmigratedConsumer\" AS SELECT \"Ticker\" FROM \"CommonStock\";",
            _ => throw new ArgumentException("Unknown scenario", nameof(scenario)),
        };
        await db.Database.ExecuteSqlRawAsync(mutation);
        var before = await Snapshot(db);
        var transaction = db.Database.CurrentTransaction!;
        await transaction.CreateSavepointAsync("before_retirement");
        try
        {
            await db.Database.ExecuteSqlRawAsync(sql);
            throw new InvalidOperationException("Retirement accepted incomplete reconciliation");
        }
        catch (PostgresException error)
        {
            var expected = scenario switch
            {
                "price-mismatch" or "unmapped-price-column" =>
                    "An original listed price is missing or changed",
                "unvalidated-owner" => "Validated owner equivalence is missing for StockSplit",
                "unarchived-source" => "Original directory evidence is incomplete",
                "unknown-dependent-view" => "cannot drop",
                "wrong-sibling-observation" =>
                    "Original listing ownership differs in DailyShortVolume",
                "wrong-price-mapping-owner" =>
                    "Original listing mapping belongs to another native issuer",
                "unknown-owner-constraint" => "Unreviewed dependencies on retired identity columns",
                "missing-native-owner-key" =>
                    "Validated native issuer foreign key is missing for StockSplit",
                "unmapped-original-issuer" => "An original directory issuer has no native identity",
                _ => throw new ArgumentException("Unknown scenario", nameof(scenario)),
            };
            Require(
                error.MessageText.Contains(expected, StringComparison.Ordinal),
                $"Retirement failed at the wrong gate: {error.MessageText}"
            );
            await transaction.RollbackToSavepointAsync("before_retirement");
        }
        Require(await Snapshot(db) == before, "Failed retirement changed native data");
        Require(
            await db
                .Database.SqlQueryRaw<int>(
                    """
                    SELECT count(*)::integer AS "Value" FROM information_schema.tables
                    WHERE table_schema = 'public' AND table_name IN ('CommonStock', 'LegacyEquityListing',
                        'DailyStockPrice', 'ListedDailyStockPrice', 'CommonStockCusipAlias', 'CommonStockTickerAlias',
                        'CommonStockTickerEvidence', 'CommonStockListedCusip', 'CommonStockDelistedListing', 'ListedSecurity')
                    """
                )
                .SingleAsync() == 10,
            "Failed retirement removed an original table"
        );
    }

    private static Task<string> Snapshot(DbContext db) =>
        db
            .Database.SqlQueryRaw<string>(
                """
                SELECT jsonb_build_object(
                    'issuers', (SELECT jsonb_agg(to_jsonb(r) - 'CommonStockId' ORDER BY "Id") FROM "EquityIssuer" r),
                    'securities', (SELECT jsonb_agg(to_jsonb(r) ORDER BY "Id") FROM "EquitySecurity" r),
                    'listings', (SELECT jsonb_agg(to_jsonb(r) ORDER BY "Id") FROM "EquityListing" r),
                    'observations', (SELECT jsonb_agg(to_jsonb(r) - 'CommonStockId' ORDER BY "Id") FROM "DailyShortVolume" r),
                    'prices', (SELECT jsonb_agg(to_jsonb(r) ORDER BY "Id") FROM "EquityDailyStockPrice" r),
                    'unattributed', (SELECT jsonb_agg(to_jsonb(r) ORDER BY "Id") FROM "UnattributedDailyStockPrice" r),
                    'aliases', (SELECT jsonb_agg(to_jsonb(r) ORDER BY "Id") FROM "EquityIssuerCusipAlias" r),
                    'sources', (SELECT jsonb_agg(to_jsonb(r) ORDER BY "Id") FROM "EquityDirectorySourceRecord" r),
                    'splits', (SELECT jsonb_agg(to_jsonb(r) - 'CommonStockId' ORDER BY "Id") FROM "StockSplit" r),
                    'dividends', (SELECT jsonb_agg(to_jsonb(r) - 'CommonStockId' ORDER BY "Id") FROM "CashDividend" r),
                    'cursor', (SELECT jsonb_agg(to_jsonb(r) - 'LastCommonStockId' - 'LastListedTicker' ORDER BY "Name") FROM "CorporateActionPriceReconciliationCursor" r)
                )::text AS "Value"
                """
            )
            .SingleAsync();

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
