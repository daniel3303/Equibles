using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Adds ListedTicker to the holder covering index and keys the NPORT CUSIP index on
    /// (Cusip, NportFilingId), then creates the lane-stamp tables. An index that still has the old
    /// definition is renamed aside and keeps serving while its replacement builds concurrently
    /// under the canonical name; the old copy is then dropped concurrently.
    /// </summary>
    public partial class PerfIndexesAndLaneStamps : Migration
    {
        private const string Holder = "IX_InstitutionalHolding_InstitutionalHolderId_ReportDate";
        private const string CusipFiling = "IX_NportHolding_CusipFiling";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Indexes first: every Replace step can be retried after an interruption, the table
            // creates below cannot.
            Replace(
                migrationBuilder,
                Holder,
                "%\"OptionType\", \"ListedTicker\")%",
                "ON \"InstitutionalHolding\" (\"InstitutionalHolderId\", \"ReportDate\") "
                    + "INCLUDE (\"EquityIssuerId\", \"Value\", \"Shares\", \"FilingDate\", "
                    + "\"FilingType\", \"OptionType\", \"ListedTicker\")"
            );
            Replace(
                migrationBuilder,
                CusipFiling,
                "%(\"Cusip\", \"NportFilingId\")%",
                "ON \"NportHolding\" (\"Cusip\", \"NportFilingId\")"
            );

            migrationBuilder.CreateTable(
                name: "FundScoreAttempt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstitutionalHolderId = table.Column<Guid>(type: "uuid", nullable: false),
                    BenchmarkTicker = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    WindowYears = table.Column<int>(type: "integer", nullable: false),
                    CalculationVersion = table.Column<int>(type: "integer", nullable: false),
                    AttemptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundScoreAttempt", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FundScoreAttempt_InstitutionalHolder_InstitutionalHolderId",
                        column: x => x.InstitutionalHolderId,
                        principalTable: "InstitutionalHolder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkerCycle",
                columns: table => new
                {
                    WorkerName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerCycle", x => x.WorkerName);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FundScoreAttempt_InstitutionalHolderId_WindowYears_Benchmar~",
                table: "FundScoreAttempt",
                columns: new[] { "InstitutionalHolderId", "WindowYears", "BenchmarkTicker" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FundScoreAttempt");

            migrationBuilder.DropTable(
                name: "WorkerCycle");

            Replace(
                migrationBuilder,
                Holder,
                "%\"FilingType\", \"OptionType\")%",
                "ON \"InstitutionalHolding\" (\"InstitutionalHolderId\", \"ReportDate\") "
                    + "INCLUDE (\"EquityIssuerId\", \"Value\", \"Shares\", \"FilingDate\", "
                    + "\"FilingType\", \"OptionType\")"
            );
            Replace(
                migrationBuilder,
                CusipFiling,
                "%(\"Cusip\") INCLUDE (\"NportFilingId\")%",
                "ON \"NportHolding\" (\"Cusip\") INCLUDE (\"NportFilingId\")"
            );
        }

        // Brings the index under `name` to `definition`. `wanted` is a LIKE pattern that only the
        // wanted definition matches, so a database that already carries it is left untouched and
        // every step can be retried after an interruption.
        private static void Replace(
            MigrationBuilder migrationBuilder,
            string name,
            string wanted,
            string definition
        )
        {
            var old = name + "_old";
            // Renaming takes only SHARE UPDATE EXCLUSIVE, and the renamed index keeps serving
            // queries until its replacement exists. Two copies at once would mean a manual rebuild
            // of the canonical name in between, which this migration refuses to guess about.
            migrationBuilder.Sql(
                "DO $$ DECLARE current text; BEGIN "
                    + $"SELECT pg_get_indexdef(c.oid) INTO current FROM pg_class c WHERE c.relname = '{name}'; "
                    + $"IF current IS NOT NULL AND current NOT LIKE '{wanted}' THEN "
                    + $"IF EXISTS (SELECT 1 FROM pg_class WHERE relname = '{old}') THEN "
                    + $"RAISE EXCEPTION 'both {name} and {old} exist; drop one before migrating'; END IF; "
                    + $"EXECUTE 'ALTER INDEX \"{name}\" RENAME TO \"{old}\"'; END IF; END $$;",
                suppressTransaction: true
            );
            // An interrupted CONCURRENTLY build leaves an invalid index that IF NOT EXISTS would keep.
            // Renaming it aside takes only SHARE UPDATE EXCLUSIVE; a plain DROP would queue every
            // reader and writer of the table behind an ACCESS EXCLUSIVE lock. The short suffix keeps
            // the longest name under PostgreSQL's 63-character limit.
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + $"WHERE c.relname = '{name}' AND NOT i.indisvalid) THEN "
                    + $"EXECUTE 'ALTER INDEX \"{name}\" RENAME TO \"{name}_inv\"'; END IF; END $$;",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                $"DROP INDEX CONCURRENTLY IF EXISTS \"{name}_inv\";",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                $"CREATE INDEX CONCURRENTLY IF NOT EXISTS \"{name}\" {definition};",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                $"DROP INDEX CONCURRENTLY IF EXISTS \"{old}\";",
                suppressTransaction: true
            );
        }
    }
}
