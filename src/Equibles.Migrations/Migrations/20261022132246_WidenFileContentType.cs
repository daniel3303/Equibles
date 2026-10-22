using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Widens File.ContentType so Office uploads (xlsx is 65 characters) fit. Raising a varchar
    /// limit rewrites nothing, but the ALTER still needs an ACCESS EXCLUSIVE lock on a table every
    /// file read joins, so it takes a short lock_timeout and retries instead of queueing readers.
    /// </summary>
    public partial class WidenFileContentType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                WithLockRetry(
                    "ALTER TABLE \"File\" ALTER COLUMN \"ContentType\" TYPE character varying(255)"
                )
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ContentType",
                table: "File",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);
        }

        // Runs one DDL statement under a 3 s lock_timeout, sleeping between attempts for up to
        // twenty minutes; the timeout is reset so later migrations in the same batch keep the default.
        private static string WithLockRetry(string statement) =>
            "DO $$ DECLARE attempt int := 0; BEGIN LOOP attempt := attempt + 1; "
            + "BEGIN SET LOCAL lock_timeout = '3s'; "
            + $"EXECUTE '{statement.Replace("'", "''")}'; SET LOCAL lock_timeout TO DEFAULT; RETURN; "
            + "EXCEPTION WHEN lock_not_available THEN "
            + "IF attempt >= 200 THEN RAISE; END IF; PERFORM pg_sleep(3); END; "
            + "END LOOP; END $$;";
    }
}
