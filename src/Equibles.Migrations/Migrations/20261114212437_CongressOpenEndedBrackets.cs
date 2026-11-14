using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class CongressOpenEndedBrackets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IncomeOpenEnded",
                table: "CongressionalDisclosureLine",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RangeOpenEnded",
                table: "CongressionalDisclosureLine",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NetWorthMaximumOpenEnded",
                table: "CongressionalAnnualDisclosure",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NetWorthMinimumOpenEnded",
                table: "CongressionalAnnualDisclosure",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Stored lines keep a ceiling-less bracket as its floor on both sides, and the forms
            // print only these: "Over $1,000,000" (spouse and dependent-child category) and
            // "Over $50,000,000" for a value, "Over $1,000,000" and "Over $5,000,000" for income.
            // The amounts keep out income lines whose ceiling was lost in extraction, which also
            // store one figure twice.
            migrationBuilder.Sql(
                """
                UPDATE "CongressionalDisclosureLine"
                SET "RangeOpenEnded" = TRUE
                WHERE "RangeMinimum" = "RangeMaximum"
                  AND "RangeMinimum" IN (1000000, 50000000);

                UPDATE "CongressionalDisclosureLine"
                SET "IncomeOpenEnded" = TRUE
                WHERE "IncomeMinimum" = "IncomeMaximum"
                  AND "IncomeMinimum" IN (1000000, 5000000);

                UPDATE "CongressionalAnnualDisclosure" AS disclosure
                SET "NetWorthMaximumOpenEnded" = EXISTS (
                        SELECT 1 FROM "CongressionalDisclosureLine" AS line
                        WHERE line."CongressionalAnnualDisclosureId" = disclosure."Id"
                          AND line."RangeOpenEnded" AND line."Kind" = 0),
                    "NetWorthMinimumOpenEnded" = EXISTS (
                        SELECT 1 FROM "CongressionalDisclosureLine" AS line
                        WHERE line."CongressionalAnnualDisclosureId" = disclosure."Id"
                          AND line."RangeOpenEnded" AND line."Kind" = 1)
                WHERE EXISTS (
                    SELECT 1 FROM "CongressionalDisclosureLine" AS line
                    WHERE line."CongressionalAnnualDisclosureId" = disclosure."Id"
                      AND line."RangeOpenEnded");
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IncomeOpenEnded",
                table: "CongressionalDisclosureLine");

            migrationBuilder.DropColumn(
                name: "RangeOpenEnded",
                table: "CongressionalDisclosureLine");

            migrationBuilder.DropColumn(
                name: "NetWorthMaximumOpenEnded",
                table: "CongressionalAnnualDisclosure");

            migrationBuilder.DropColumn(
                name: "NetWorthMinimumOpenEnded",
                table: "CongressionalAnnualDisclosure");
        }
    }
}
