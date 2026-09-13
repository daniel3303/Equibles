using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(HistoricalEquityDbCollection.Name)]
public class NativeIssuerFinancialFactsTests(HistoricalEquityDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    [Fact]
    public async Task Migration_PreservesCompleteFactsAndStatements_WhenLegacyOwnerIsRemoved()
    {
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        await DbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TEMP TABLE "CommonStock" ("Id" uuid PRIMARY KEY) ON COMMIT DROP;
            CREATE TEMP TABLE "EquityIssuer" ("Id" uuid PRIMARY KEY) ON COMMIT DROP;
            INSERT INTO "CommonStock" VALUES ('00000000-0000-0000-0000-000000000001');
            INSERT INTO "EquityIssuer" SELECT * FROM "CommonStock";
            CREATE TEMP TABLE "FinancialFact" (
                "Id" uuid PRIMARY KEY, "CommonStockId" uuid NOT NULL, "FinancialConceptId" uuid NOT NULL,
                "DocumentId" uuid, "Unit" varchar(32) NOT NULL, "PeriodType" integer NOT NULL,
                "PeriodStart" date NOT NULL, "PeriodEnd" date NOT NULL, "Value" numeric NOT NULL,
                "FiscalYear" integer NOT NULL, "FiscalPeriod" integer NOT NULL, "Form" text NOT NULL,
                "FiledDate" date NOT NULL, "AccessionNumber" varchar(32) NOT NULL, "Frame" varchar(32),
                "DimensionsKey" varchar(64) NOT NULL, "CreationTime" timestamptz NOT NULL,
                CONSTRAINT "FK_FinancialFact_CommonStock_CommonStockId" FOREIGN KEY ("CommonStockId")
                    REFERENCES "CommonStock"("Id") ON DELETE CASCADE
            ) ON COMMIT DROP;
            CREATE TEMP TABLE "ReportedFinancialStatement" (
                "Id" uuid PRIMARY KEY, "CommonStockId" uuid NOT NULL, "DocumentId" uuid NOT NULL,
                "AccessionNumber" varchar(32) NOT NULL, "Kind" integer NOT NULL, "RoleUri" varchar(512) NOT NULL,
                "RoleShortName" varchar(512), "ReportFileName" varchar(64), "IsParenthetical" boolean NOT NULL,
                "FiscalYear" integer NOT NULL, "FiscalPeriod" integer NOT NULL, "PrimaryPeriodEnd" date NOT NULL,
                "Form" text NOT NULL, "FiledDate" date NOT NULL, "Position" integer NOT NULL,
                "Currency" varchar(16), "Scale" bigint NOT NULL, "Payload" jsonb NOT NULL,
                "CreationTime" timestamptz NOT NULL,
                CONSTRAINT "FK_ReportedFinancialStatement_CommonStock_CommonStockId" FOREIGN KEY ("CommonStockId")
                    REFERENCES "CommonStock"("Id") ON DELETE CASCADE
            ) ON COMMIT DROP;
            INSERT INTO "FinancialFact" VALUES
                ('00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000001',
                '00000000-0000-0000-0000-000000000003', NULL, 'EUR', 1, '2025-01-01', '2025-12-31',
                -123456789012345.123456789, 2025, 0, 'TwentyF', '2026-03-15', 'original', NULL,
                'dimensional-source-key', '2026-03-16T01:02:03.123456Z');
            INSERT INTO "FinancialFact" SELECT '00000000-0000-0000-0000-000000000004', "CommonStockId",
                "FinancialConceptId", "DocumentId", "Unit", "PeriodType", "PeriodStart", "PeriodEnd",
                123456789012346.987654321, "FiscalYear", "FiscalPeriod", "Form", '2026-04-15', 'restated',
                "Frame", "DimensionsKey", '2026-04-16T01:02:03.123456Z' FROM "FinancialFact";
            INSERT INTO "ReportedFinancialStatement" VALUES
                ('00000000-0000-0000-0000-000000000005', '00000000-0000-0000-0000-000000000001',
                '00000000-0000-0000-0000-000000000006', 'original', 1, 'https://issuer.example/role/income',
                'Demonstração dos resultados', 'R2.htm', false, 2025, 0, '2025-12-31', 'TwentyF', '2026-03-15',
                7, 'EUR', 1000000, jsonb_build_object('columns', jsonb_build_array(jsonb_build_object('periodEnd', '2025-12-31')), 'rows', jsonb_build_array(jsonb_build_object('label', 'Receita', 'values', jsonb_build_array(123.456789, NULL)))),
                '2026-03-16T01:02:03.123456Z');
            """
        );
        var before = await Snapshot();
        var commands = DbContext
            .GetService<IMigrationsSqlGenerator>()
            .Generate(new RetargetFinancialFactsToIssuers().UpOperations);
        foreach (var command in commands)
            await DbContext.Database.ExecuteSqlRawAsync(command.CommandText);
        (await Snapshot()).Should().Be(before);
        await DbContext.Database.ExecuteSqlRawAsync("""DELETE FROM "CommonStock";""");
        (await Snapshot()).Should().Be(before);
    }

    [Fact]
    public async Task NativeIssuer_CanStoreRestatementsAndDimensions_WithoutALegacyStock()
    {
        var issuer = new EquityIssuer { Name = "Native reporting issuer" };
        var concept = new FinancialConcept
        {
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "Revenues",
            Label = "Revenue",
        };
        DbContext.AddRange(issuer, concept);
        await DbContext.SaveChangesAsync();
        FinancialFact Fact(string accession, string dimension, decimal value) =>
            new()
            {
                EquityIssuerId = issuer.Id,
                FinancialConceptId = concept.Id,
                Unit = "EUR",
                Value = value,
                PeriodType = FactPeriodType.Duration,
                PeriodStart = new(2025, 1, 1),
                PeriodEnd = new(2025, 12, 31),
                FiscalYear = 2025,
                FiscalPeriod = SecFiscalPeriod.FullYear,
                Form = DocumentType.TwentyF,
                FiledDate = new(2026, 3, 15),
                AccessionNumber = accession,
                DimensionsKey = dimension,
            };
        var facts = new[]
        {
            Fact("original", "", 120.25m),
            Fact("restated", "", 121.75m),
            Fact("restated", "segment", 60.5m),
        };
        await DbContext
            .Set<FinancialFact>()
            .UpsertRange(facts)
            .On(f => new
            {
                f.EquityIssuerId,
                f.FinancialConceptId,
                f.Unit,
                f.PeriodStart,
                f.PeriodEnd,
                f.AccessionNumber,
                f.DimensionsKey,
            })
            .RunAsync();
        var repository = new FinancialFactRepository(DbContext);
        var stored = await repository.GetByIssuerId(issuer.Id).OrderBy(f => f.Value).ToListAsync();
        stored.Select(f => f.Id).Should().BeEquivalentTo(facts.Select(f => f.Id));
        stored.Select(f => f.Value).Should().Equal(60.5m, 120.25m, 121.75m);
        (await repository.GetConsolidatedByIssuerId(issuer.Id).CountAsync()).Should().Be(2);
        (await DbContext.Set<CommonStock>().CountAsync()).Should().Be(0);
        (await DbContext.Set<EquityListing>().CountAsync()).Should().Be(0);
    }

    private Task<string> Snapshot() =>
        DbContext
            .Database.SqlQueryRaw<string>(
                """
                SELECT jsonb_build_array(
                    (SELECT jsonb_agg(to_jsonb(f) ORDER BY "Id") FROM "FinancialFact" f),
                    (SELECT jsonb_agg(to_jsonb(s) ORDER BY "Id") FROM "ReportedFinancialStatement" s)
                )::text AS "Value"
                """
            )
            .SingleAsync();
}
