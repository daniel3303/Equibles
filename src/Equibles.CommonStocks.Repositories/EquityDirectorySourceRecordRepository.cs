using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Repositories;

public class EquityDirectorySourceRecordRepository(EquiblesFinancialDbContext dbContext)
    : BaseRepository<EquityDirectorySourceRecord>(dbContext)
{
    // JSONB canonicalization gives every source the same hash/equality contract.
    public Task<Guid> Append(
        string source,
        string key,
        string payloadJson,
        CancellationToken cancellationToken = default
    ) => Append(DbContext, source, key, payloadJson, cancellationToken);

    internal static async Task<Guid> Append(
        EquiblesFinancialDbContext context,
        string source,
        string key,
        string payloadJson,
        CancellationToken cancellationToken
    )
    {
        if (context.Database.CurrentTransaction == null)
            throw new InvalidOperationException(
                "Source capture requires the caller's identity transaction."
            );
        var id = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "EquityDirectorySourceRecord"
                ("Id", "Source", "SourceRecordKey", "PayloadHash", "PayloadJson", "CapturedAt")
            SELECT {id}, {source}, {key},
                encode(sha256(convert_to({payloadJson}::jsonb::text, 'UTF8')), 'hex'),
                {payloadJson}::jsonb, now()
            ON CONFLICT ("Source", "SourceRecordKey", "PayloadHash") DO NOTHING
            """,
            cancellationToken
        );
        var stored = await context
            .Database.SqlQuery<Guid>(
                $"""
                SELECT "Id" AS "Value" FROM "EquityDirectorySourceRecord"
                WHERE "Source" = {source} AND "SourceRecordKey" = {key}
                    AND "PayloadHash" = encode(sha256(convert_to({payloadJson}::jsonb::text, 'UTF8')), 'hex')
                    AND "PayloadJson" = {payloadJson}::jsonb
                """
            )
            .SingleOrDefaultAsync(cancellationToken);
        if (stored == Guid.Empty)
            throw new InvalidDataException(
                "Source evidence hash conflicts with its stored payload."
            );
        return stored;
    }
}
