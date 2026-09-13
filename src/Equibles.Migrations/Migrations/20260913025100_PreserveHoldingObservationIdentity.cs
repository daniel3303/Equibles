using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.Migrations.Migrations;

public partial class PreserveHoldingObservationIdentity : Migration {
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.Sql("""
CREATE OR REPLACE FUNCTION public.eq_preserve_holding_observation_identity()
RETURNS trigger LANGUAGE plpgsql AS $fn$
DECLARE observed record; matched boolean := false; retained_ticker text;
BEGIN
    IF NEW."Cusip" IS NULL THEN RETURN NEW; END IF;
    -- The canonical-owner bridge runs first for retiring binaries. All generations then
    -- serialize on the same native issuer before resolving this observation's retained key.
    PERFORM 1 FROM "EquityIssuer" WHERE "Id" = NEW."EquityIssuerId" FOR NO KEY UPDATE;
    FOR observed IN SELECT "ListedTicker" FROM "InstitutionalHolding"
      WHERE "EquityIssuerId" = NEW."EquityIssuerId"
        AND "InstitutionalHolderId" = NEW."InstitutionalHolderId"
        AND "ReportDate" = NEW."ReportDate" AND "Cusip" = NEW."Cusip"
        AND "ShareType" = NEW."ShareType" AND "FilingType" = NEW."FilingType"
        AND "OptionType" IS NOT DISTINCT FROM NEW."OptionType"
      LIMIT 2
    LOOP
        IF matched THEN
            RAISE EXCEPTION 'Conflicting stored observation identities; holding replay was refused' USING ERRCODE = '23514';
        END IF;
        matched := true;
        retained_ticker := observed."ListedTicker";
    END LOOP;
    -- Never redirect an unprepared writer: its later manager-entry assembly still uses
    -- its original key and could overwrite another security's allocations.
    IF matched AND NEW."ListedTicker" IS DISTINCT FROM retained_ticker THEN
        RAISE EXCEPTION 'Holding observation identity changed; reload its stored key before replay' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $fn$;
CREATE TRIGGER equity_holding_observation_identity
BEFORE INSERT ON "InstitutionalHolding"
FOR EACH ROW EXECUTE FUNCTION public.eq_preserve_holding_observation_identity();
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Retained holding observation identity must remain protected.");
}
