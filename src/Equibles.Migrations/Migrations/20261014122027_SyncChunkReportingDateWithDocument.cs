using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class SyncChunkReportingDateWithDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Chunk.ReportingDate is the BM25-indexed copy of its document's filing date at midnight
            // UTC. Both triggers make that equality hold for every writer: a chunk always takes its
            // parent's date, and a filing-date correction moves the document's chunks with it.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public."InheritChunkReportingDate"()
                 RETURNS trigger
                 LANGUAGE plpgsql
                AS $function$
                BEGIN
                    SELECT d."ReportingDate"::timestamp AT TIME ZONE 'UTC'
                    INTO NEW."ReportingDate"
                    FROM "Document" AS d
                    WHERE d."Id" = NEW."DocumentId";
                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER "TR_Chunk_InheritReportingDate"
                BEFORE INSERT OR UPDATE OF "ReportingDate", "DocumentId" ON "Chunk"
                FOR EACH ROW
                EXECUTE FUNCTION public."InheritChunkReportingDate"();

                CREATE OR REPLACE FUNCTION public."SyncChunkReportingDate"()
                 RETURNS trigger
                 LANGUAGE plpgsql
                AS $function$
                BEGIN
                    UPDATE "Chunk"
                    SET "ReportingDate" = NEW."ReportingDate"::timestamp AT TIME ZONE 'UTC'
                    WHERE "DocumentId" = NEW."Id"
                      AND "ReportingDate" IS DISTINCT FROM
                          (NEW."ReportingDate"::timestamp AT TIME ZONE 'UTC');
                    RETURN NULL;
                END;
                $function$;

                CREATE TRIGGER "TR_Document_SyncChunkReportingDate"
                AFTER UPDATE OF "ReportingDate" ON "Document"
                FOR EACH ROW
                WHEN (OLD."ReportingDate" IS DISTINCT FROM NEW."ReportingDate")
                EXECUTE FUNCTION public."SyncChunkReportingDate"();

                UPDATE "Chunk" AS c
                SET "ReportingDate" = (d."ReportingDate"::timestamp AT TIME ZONE 'UTC')
                FROM "Document" AS d
                WHERE c."DocumentId" = d."Id"
                  AND c."ReportingDate" IS DISTINCT FROM
                      (d."ReportingDate"::timestamp AT TIME ZONE 'UTC');
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS "TR_Document_SyncChunkReportingDate" ON "Document";
                DROP FUNCTION IF EXISTS public."SyncChunkReportingDate"();
                DROP TRIGGER IF EXISTS "TR_Chunk_InheritReportingDate" ON "Chunk";
                DROP FUNCTION IF EXISTS public."InheritChunkReportingDate"();
                """
            );
        }
    }
}
