using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Migrations.Migrations;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Pgvector;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class NativeIssuerFilingGraphTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FilingGraph_PreservesAllStoredFields_WithoutALegacyOwner(
        bool migrateLegacyOwner
    )
    {
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        EquityIssuer issuer;
        if (migrateLegacyOwner)
        {
            var oldOwner = new CommonStock
            {
                Name = "Issuer graph",
                Ticker = "GRAPH",
                Cik = "0000000099",
            };
            DbContext.Add(oldOwner);
            await DbContext.SaveChangesAsync();
            issuer = await DbContext.Set<EquityIssuer>().SingleAsync(row => row.Id == oldOwner.Id);
        }
        else
        {
            issuer = new EquityIssuer { Name = "Issuer without a listing", Cik = "0000000099" };
            DbContext.Add(issuer);
        }

        var file = new File
        {
            Name = "Original café",
            Extension = "html",
            ContentType = "text/html",
            Size = 5,
            FileContent = new FileContent { Bytes = [0, 1, 127, 128, 255] },
        };
        var document = new Document
        {
            Issuer = issuer,
            Content = file,
            DocumentType = DocumentType.TwentyF,
            AccessionNumber = "0000000099-25-000001",
            ReportingDate = new DateOnly(2025, 3, 2),
            ReportingForDate = new DateOnly(2024, 12, 31),
            SourceUrl = "https://example.com/original",
            ChunkedAt = new DateTime(2025, 3, 2, 1, 2, 3, DateTimeKind.Utc),
            ChunkAttempts = 2,
            Chunks =
            [
                new Chunk
                {
                    Index = 0,
                    Content = "Original € evidence",
                    StartLineNumber = 7,
                },
            ],
            Images = [new DocumentImage { FileName = "original.png", File = file }],
            Artifacts =
            [
                new SecFilingArtifact
                {
                    FileName = "exhibit.htm",
                    Type = "EX-10",
                    SourceUrl = "https://example.com/exhibit",
                    Content = "Source text €",
                    CaptureStatus = SecFilingArtifactCaptureStatus.TextCaptured,
                },
            ],
        };
        DbContext.Add(document);
        DbContext.Add(
            new Embedding
            {
                Chunk = document.Chunks[0],
                Model = "preservation-test",
                Vector = new Vector(new float[] { 0.25f, -0.5f }),
                VectorDimension = 2,
            }
        );
        DbContext.Add(
            new FinancialFact
            {
                Issuer = issuer,
                Document = document,
                FinancialConcept = new FinancialConcept { Tag = "Assets" },
                Unit = "EUR",
                Value = 123456789.123456789m,
                Form = document.DocumentType,
                AccessionNumber = document.AccessionNumber,
                PeriodStart = document.ReportingForDate,
                PeriodEnd = document.ReportingForDate,
                FiledDate = document.ReportingDate,
            }
        );
        DbContext.Add(
            new ReportedFinancialStatement
            {
                Issuer = issuer,
                Document = document,
                AccessionNumber = document.AccessionNumber,
                RoleUri = "https://example.com/role",
                Form = document.DocumentType,
                Currency = "EUR",
                Payload = "{\"label\":\"Ativos €\",\"value\":123456789.123456789}",
            }
        );
        DbContext.Add(
            new FormDFiling
            {
                Issuer = issuer,
                AccessionNumber = "form-d",
                FilingDate = document.ReportingDate,
                EntityName = "Original entity",
                TotalAmountSold = 987654321,
                RelatedPersons =
                [
                    new FormDRelatedPerson { Name = "Original person", Relationships = "Director" },
                ],
            }
        );
        DbContext.Add(
            new NCenFiling
            {
                Issuer = issuer,
                AccessionNumber = "n-cen",
                FilingDate = document.ReportingDate,
                RegistrantName = "Original registrant",
                Country = "PT",
                ServiceProviders =
                [
                    new NCenServiceProvider { Name = "Original provider", Country = "PT" },
                ],
            }
        );
        DbContext.Add(
            new NportFiling
            {
                Issuer = issuer,
                AccessionNumber = "nport",
                FilingDate = document.ReportingDate,
                SeriesId = "S000000001",
                NetAssets = 123456.789m,
                Holdings =
                [
                    new NportHolding
                    {
                        Name = "Original holding",
                        Currency = "EUR",
                        Balance = 123.456m,
                    },
                ],
            }
        );
        // The unlinked trust cohort must remain unlinked and retain its source CIK.
        DbContext.Add(
            new NportFiling { RegistrantCik = "0000000088", AccessionNumber = "unlinked-nport" }
        );
        await DbContext.SaveChangesAsync();

        if (migrateLegacyOwner)
        {
            // Reconstruct only the four pre-migration owner constraints on the real schema.
            // Every document-child FK remains present, including facts, images and embeddings.
            foreach (var table in new[] { "Document", "FormDFiling", "NCenFiling", "NportFiling" })
            {
                await DbContext.Database.ExecuteSqlRawAsync(
                    $"""
                    ALTER TABLE "{table}" DROP CONSTRAINT "FK_{table}_EquityIssuer_CommonStockId";
                    ALTER TABLE "{table}" ADD CONSTRAINT "FK_{table}_CommonStock_CommonStockId"
                        FOREIGN KEY ("CommonStockId") REFERENCES "CommonStock"("Id") ON DELETE CASCADE;
                    """
                );
            }
        }
        var before = await Snapshot();
        if (migrateLegacyOwner)
        {
            var commands = DbContext
                .GetService<IMigrationsSqlGenerator>()
                .Generate(new RetargetFilingsToIssuers().UpOperations);
            foreach (var command in commands)
                await DbContext.Database.ExecuteSqlRawAsync(command.CommandText);
            (await Snapshot()).Should().Be(before);
            await DbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "EquityIssuer" SET "CommonStockId" = NULL WHERE "Id" = {issuer.Id};
                DELETE FROM "CommonStock" WHERE "Id" = {issuer.Id};
                """
            );
        }
        (await Snapshot()).Should().Be(before);
        DbContext.ChangeTracker.Clear();
        var stored = await new DocumentRepository(DbContext).GetByIssuerId(issuer.Id).SingleAsync();
        stored.Id.Should().Be(document.Id);
        stored.Issuer.Cik.Should().Be("0000000099");
        stored.Chunks.Should().ContainSingle().Which.Content.Should().Be("Original € evidence");
        (await DbContext.Set<CommonStock>().CountAsync()).Should().Be(0);
        Func<Task> deleteIssuer = () =>
            DbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""DELETE FROM "EquityIssuer" WHERE "Id" = {issuer.Id}"""
            );
        (await deleteIssuer.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should()
            .Be(PostgresErrorCodes.RestrictViolation);
    }

    private async Task<string> Snapshot()
    {
        var snapshots = new List<string>();
        // Closed identifier list: compare every column, including binary content and precision.
        foreach (
            var table in new[]
            {
                "Document",
                "Chunk",
                "Embedding",
                "DocumentImage",
                "SecFilingArtifact",
                "File",
                "FileContent",
                "FinancialFact",
                "ReportedFinancialStatement",
                "FormDFiling",
                "FormDRelatedPerson",
                "NCenFiling",
                "NCenServiceProvider",
                "NportFiling",
                "NportHolding",
            }
        )
            snapshots.Add(
                await DbContext
                    .Database.SqlQueryRaw<string>(
                        $"""SELECT COALESCE(jsonb_agg(to_jsonb(row) ORDER BY "Id"), '[]'::jsonb)::text AS "Value" FROM "{table}" row"""
                    )
                    .SingleAsync()
            );
        return string.Join("\n", snapshots);
    }
}
