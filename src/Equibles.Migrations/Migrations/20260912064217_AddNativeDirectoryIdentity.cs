using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddNativeDirectoryIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MarketCountryCode",
                table: "EquityListing",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalEntityIdentifier",
                table: "EquityIssuer",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquityListing_MarketCountryCode_Ticker",
                table: "EquityListing",
                columns: new[] { "MarketCountryCode", "Ticker" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_EquityListing_MarketCountryCode",
                table: "EquityListing",
                sql: "\"MarketCountryCode\" ~ '^[A-Z]{2}$'");

            migrationBuilder.CreateIndex(
                name: "IX_EquityIssuer_LegalEntityIdentifier",
                table: "EquityIssuer",
                column: "LegalEntityIdentifier",
                unique: true);

            // Every pre-international directory row is a U.S. listing, including foreign issuers
            // and OTC securities. No issuer domicile or quotation currency is used as evidence.
            migrationBuilder.Sql("""
                UPDATE "EquityListing" listing
                SET "MarketCountryCode" = 'US'
                FROM "LegacyEquityListing" mapping
                WHERE mapping."EquityListingId" = listing."Id";

                CREATE OR REPLACE FUNCTION public.eq_set_legacy_listing_market()
                RETURNS trigger LANGUAGE plpgsql AS $function$
                DECLARE market text;
                BEGIN
                    SELECT "MarketCountryCode" INTO market FROM "EquityListing"
                    WHERE "Id" = NEW."EquityListingId" FOR UPDATE;
                    IF market IS NOT NULL AND market <> 'US' THEN
                        RAISE EXCEPTION 'A U.S. directory identity cannot adopt a foreign listing';
                    END IF;
                    UPDATE "EquityListing" SET "MarketCountryCode" = 'US'
                    WHERE "Id" = NEW."EquityListingId" AND "MarketCountryCode" IS NULL;
                    RETURN NEW;
                END $function$;
                CREATE TRIGGER eq_legacy_listing_market
                AFTER INSERT OR UPDATE OF "EquityListingId" ON "LegacyEquityListing"
                FOR EACH ROW EXECUTE FUNCTION public.eq_set_legacy_listing_market();
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Removing native directory identity would discard market and legal-entity evidence.");
        }
    }
}
