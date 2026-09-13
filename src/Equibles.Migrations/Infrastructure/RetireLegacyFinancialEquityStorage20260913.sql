-- Frozen final contract body. Execute in ONE caller-owned transaction only after native
-- binaries, complete original-row reconciliation, and live verification have passed.
-- SHARE ROW EXCLUSIVE keeps reads available while freezing the compared source/target pairs.
SET LOCAL lock_timeout = '5s';
SET LOCAL timezone = 'UTC';
SET LOCAL extra_float_digits = 3;

LOCK TABLE "CommonStock", "CommonStockCusipAlias", "CommonStockDelistedListing", "CommonStockListedCusip", "CommonStockTickerAlias", "CommonStockTickerEvidence", "CorporateActionPriceReconciliationCursor", "DailyShortVolume", "DailyStockPrice", "EquityDailyStockPrice", "EquityDirectorySourceRecord", "EquityIssuer", "EquityIssuerCusipAlias", "EquityIssuerTickerAlias", "EquityIssuerTickerEvidence", "EquityListing", "EquityListingCusipEvidence", "EquityListingRetirementEvidence", "EquitySecurity", "FailToDeliver", "IssuerSecurityRegistration", "LegacyEquityListing", "ListedDailyStockPrice", "ListedSecurity", "OffExchangeVolume", "ShortInterest", "UnattributedDailyStockPrice" IN SHARE ROW EXCLUSIVE MODE;

DO $owners$
DECLARE target record;
BEGIN
    FOR target IN SELECT * FROM (VALUES
        ('CommonStockCusipAlias', 'CommonStockId'),
        ('CommonStockTickerAlias', 'CommonStockId'),
        ('CommonStockTickerEvidence', 'CommonStockId'),
        ('CommonStockListedCusip', 'CommonStockId'),
        ('CommonStockDelistedListing', 'CommonStockId'),
        ('CongressionalTrade', 'CommonStockId'),
        ('CashDividend', 'CommonStockId'),
        ('StockSplit', 'CommonStockId'),
        ('FdaCatalyst', 'CommonStockId'),
        ('GovernmentContract', 'CommonStockId'),
        ('InstitutionalHolding', 'CommonStockId'),
        ('StockQuarterlyActivity', 'CommonStockId'),
        ('StockQuarterlyActivityCombined', 'CommonStockId'),
        ('StockQuarterlyListingActivity', 'CommonStockId'),
        ('Form144Filing', 'CommonStockId'),
        ('InsiderTransaction', 'CommonStockId'),
        ('CompanyFilingSyncState', 'CommonStockId'),
        ('Document', 'CommonStockId'),
        ('FormDFiling', 'CommonStockId'),
        ('FundSeries', 'CommonStockId'),
        ('NCenFiling', 'CommonStockId'),
        ('NportFiling', 'CommonStockId'),
        ('TranscriptCheckStatuses', 'CommonStockId'),
        ('FinancialFact', 'CommonStockId'),
        ('FinancialFactsSyncStatus', 'CommonStockId'),
        ('ListedSecurity', 'CommonStockId'),
        ('ReportedFinancialStatement', 'CommonStockId')
    ) expected(table_name, previous_column)
    LOOP
        IF NOT EXISTS (SELECT 1 FROM "EquityOwnerMigrationProgress"
            WHERE "TableName" = target.table_name AND "Completed") THEN
            RAISE EXCEPTION 'Owner migration is incomplete for %', target.table_name;
        END IF;
        -- The validated equality constraint proves every old/new owner pair, including NULLs.
        -- It remains enforced until DROP COLUMN; no long owner-table scan holds DDL locks.
        IF NOT EXISTS (SELECT 1 FROM pg_constraint
            WHERE conrelid = to_regclass(format('%I', target.table_name))
              AND conname = ('CK_' || target.table_name || '_CanonicalOwnerMirror')::name
              AND contype = 'c' AND convalidated
              AND pg_get_constraintdef(oid) = format('CHECK ((NOT ("EquityIssuerId" IS DISTINCT FROM %I)))', target.previous_column)) THEN
            RAISE EXCEPTION 'Validated owner equivalence is missing for %', target.table_name;
        END IF;
        IF NOT EXISTS (SELECT 1 FROM pg_constraint c
            JOIN pg_attribute owner_column ON owner_column.attrelid = c.conrelid AND owner_column.attname = 'EquityIssuerId'
            JOIN pg_attribute issuer_key ON issuer_key.attrelid = c.confrelid AND issuer_key.attname = 'Id'
            WHERE c.conrelid = to_regclass(format('%I', target.table_name))
              AND c.contype = 'f' AND c.convalidated AND c.confdeltype = 'r'
              AND c.confrelid = '"EquityIssuer"'::regclass
              AND c.conkey = ARRAY[owner_column.attnum]::smallint[]
              AND c.confkey = ARRAY[issuer_key.attnum]::smallint[]) THEN
            RAISE EXCEPTION 'Validated native issuer foreign key is missing for %', target.table_name;
        END IF;
    END LOOP;
    IF EXISTS (SELECT 1 FROM (VALUES ('DailyStockPrice'), ('ListedDailyStockPrice')) expected(table_name)
        WHERE NOT EXISTS (SELECT 1 FROM "NativePriceMigrationProgress" p
            WHERE p."TableName" = expected.table_name AND p."Completed")) THEN
        RAISE EXCEPTION 'Price copy is incomplete';
    END IF;
    IF EXISTS (SELECT 1 FROM (VALUES ('FailToDeliver'), ('DailyShortVolume'), ('ShortInterest'), ('OffExchangeVolume')) expected(table_name)
        WHERE NOT EXISTS (SELECT 1 FROM "NativeListingObservationMigrationProgress" p
            WHERE p."TableName" = expected.table_name AND p."Completed")) THEN
        RAISE EXCEPTION 'Listing observation copy is incomplete';
    END IF;
END $owners$;

DO $column_dependencies$
DECLARE unexpected text;
BEGIN
    WITH dropped_columns(table_name, column_name) AS (VALUES
            ('CashDividend', 'CommonStockId'),
            ('CompanyFilingSyncState', 'CommonStockId'),
            ('CongressionalTrade', 'CommonStockId'),
            ('CorporateActionPriceReconciliationCursor', 'LastCommonStockId'),
            ('CorporateActionPriceReconciliationCursor', 'LastListedTicker'),
            ('DailyShortVolume', 'CommonStockId'),
            ('Document', 'CommonStockId'),
            ('EquityIssuer', 'CommonStockId'),
            ('FailToDeliver', 'CommonStockId'),
            ('FdaCatalyst', 'CommonStockId'),
            ('FinancialFact', 'CommonStockId'),
            ('FinancialFactsSyncStatus', 'CommonStockId'),
            ('Form144Filing', 'CommonStockId'),
            ('FormDFiling', 'CommonStockId'),
            ('FundSeries', 'CommonStockId'),
            ('GovernmentContract', 'CommonStockId'),
            ('InsiderTransaction', 'CommonStockId'),
            ('InstitutionalHolding', 'CommonStockId'),
            ('NCenFiling', 'CommonStockId'),
            ('NportFiling', 'CommonStockId'),
            ('OffExchangeVolume', 'CommonStockId'),
            ('ReportedFinancialStatement', 'CommonStockId'),
            ('ShortInterest', 'CommonStockId'),
            ('StockQuarterlyActivity', 'CommonStockId'),
            ('StockQuarterlyActivityCombined', 'CommonStockId'),
            ('StockQuarterlyListingActivity', 'CommonStockId'),
            ('StockSplit', 'CommonStockId'),
            ('TranscriptCheckStatuses', 'CommonStockId')
    ), approved(table_name, column_name, object_catalog, object_name, definition) AS (VALUES
            ('CashDividend', 'CommonStockId', 'pg_class', 'IX_CashDividend_CommonStockId_ExDate', 'CREATE UNIQUE INDEX "IX_CashDividend_CommonStockId_ExDate" ON public."CashDividend" USING btree ("CommonStockId", "ExDate") WHERE ("EquityListingId" IS NULL)'),
            ('CashDividend', 'CommonStockId', 'pg_constraint', 'CK_CashDividend_CanonicalOwnerMirror', 'CHECK ((NOT ("EquityIssuerId" IS DISTINCT FROM "CommonStockId")))'),
            ('CashDividend', 'CommonStockId', 'pg_constraint', 'CashDividend_CommonStockId_not_null', 'NOT NULL "CommonStockId"'),
            ('CashDividend', 'CommonStockId', 'pg_constraint', 'FK_CashDividend_EquityIssuer_CommonStockId', 'FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT'),
            ('CompanyFilingSyncState', 'CommonStockId', 'pg_constraint', 'CK_CompanyFilingSyncState_CanonicalOwnerMirror', 'CHECK ((NOT ("EquityIssuerId" IS DISTINCT FROM "CommonStockId")))'),
            ('CompanyFilingSyncState', 'CommonStockId', 'pg_constraint', 'CompanyFilingSyncState_CommonStockId_not_null', 'NOT NULL "CommonStockId"'),
            ('CompanyFilingSyncState', 'CommonStockId', 'pg_constraint', 'FK_CompanyFilingSyncState_EquityIssuer_CommonStockId', 'FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT'),
            ('CompanyFilingSyncState', 'CommonStockId', 'pg_constraint', 'PK_CompanyFilingSyncState', 'PRIMARY KEY ("CommonStockId")'),
            ('CongressionalTrade', 'CommonStockId', 'pg_class', 'IX_CongressionalTrade_CommonStockId_TransactionDate', 'CREATE INDEX "IX_CongressionalTrade_CommonStockId_TransactionDate" ON public."CongressionalTrade" USING btree ("CommonStockId", "TransactionDate")'),
            ('CongressionalTrade', 'CommonStockId', 'pg_class', 'IX_CongressionalTrade_LegacyFilingIdentity', 'CREATE INDEX "IX_CongressionalTrade_LegacyFilingIdentity" ON public."CongressionalTrade" USING btree ("CommonStockId", "CongressMemberId", "TransactionDate", "TransactionType", "AssetName", "OwnerType", "AmountFrom", "AmountTo", "AssetType", "Subholding")'),
            ('CongressionalTrade', 'CommonStockId', 'pg_constraint', 'CK_CongressionalTrade_CanonicalOwnerMirror', 'CHECK ((NOT ("EquityIssuerId" IS DISTINCT FROM "CommonStockId")))'),
            ('CongressionalTrade', 'CommonStockId', 'pg_constraint', 'FK_CongressionalTrade_EquityIssuer_CommonStockId', 'FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT'),
            ('DailyShortVolume', 'CommonStockId', 'pg_class', 'IX_DailyShortVolume_CommonStockId_ListedTicker_Date', 'CREATE UNIQUE INDEX "IX_DailyShortVolume_CommonStockId_ListedTicker_Date" ON public."DailyShortVolume" USING btree ("CommonStockId", "ListedTicker", "Date")'),
            ('Document', 'CommonStockId', 'pg_class', 'IX_Document_CommonStockId_DocumentType', 'CREATE INDEX "IX_Document_CommonStockId_DocumentType" ON public."Document" USING btree ("CommonStockId", "DocumentType")'),
            ('Document', 'CommonStockId', 'pg_class', 'IX_Document_CommonStockId_DocumentType_ReportingDate', 'CREATE INDEX "IX_Document_CommonStockId_DocumentType_ReportingDate" ON public."Document" USING btree ("CommonStockId", "DocumentType", "ReportingDate")'),
            ('Document', 'CommonStockId', 'pg_constraint', 'CK_Document_CanonicalOwnerMirror', 'CHECK ((NOT ("EquityIssuerId" IS DISTINCT FROM "CommonStockId")))'),
            ('Document', 'CommonStockId', 'pg_constraint', 'Document_CommonStockId_not_null', 'NOT NULL "CommonStockId"'),
            ('Document', 'CommonStockId', 'pg_constraint', 'FK_Document_EquityIssuer_CommonStockId', 'FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT'),
            ('EquityIssuer', 'CommonStockId', 'pg_class', 'IX_EquityIssuer_CommonStockId', 'CREATE UNIQUE INDEX "IX_EquityIssuer_CommonStockId" ON public."EquityIssuer" USING btree ("CommonStockId")'),
            ('EquityIssuer', 'CommonStockId', 'pg_constraint', 'FK_EquityIssuer_CommonStock_CommonStockId', 'FOREIGN KEY ("CommonStockId") REFERENCES "CommonStock"("Id") ON DELETE SET NULL'),
            ('FailToDeliver', 'CommonStockId', 'pg_class', 'IX_FailToDeliver_CommonStockId_ListedTicker_SettlementDate', 'CREATE UNIQUE INDEX "IX_FailToDeliver_CommonStockId_ListedTicker_SettlementDate" ON public."FailToDeliver" USING btree ("CommonStockId", "ListedTicker", "SettlementDate")'),
            ('FdaCatalyst', 'CommonStockId', 'pg_class', 'IX_FdaCatalyst_CommonStockId', 'CREATE INDEX "IX_FdaCatalyst_CommonStockId" ON public."FdaCatalyst" USING btree ("CommonStockId")'),
            ('FdaCatalyst', 'CommonStockId', 'pg_constraint', 'CK_FdaCatalyst_CanonicalOwnerMirror', 'CHECK ((NOT ("EquityIssuerId" IS DISTINCT FROM "CommonStockId")))'),
            ('FdaCatalyst', 'CommonStockId', 'pg_constraint', 'FK_FdaCatalyst_EquityIssuer_CommonStockId', 'FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT'),
            ('FinancialFact', 'CommonStockId', 'pg_class', 'IX_FinancialFact_CommonStockId_FinancialConceptId_PeriodEnd', 'CREATE INDEX "IX_FinancialFact_CommonStockId_FinancialConceptId_PeriodEnd" ON public."FinancialFact" USING btree ("CommonStockId", "FinancialConceptId", "PeriodEnd")'),
            ('FinancialFact', 'CommonStockId', 'pg_class', 'IX_FinancialFact_CommonStockId_FinancialConceptId_Unit_PeriodS~', 'CREATE UNIQUE INDEX "IX_FinancialFact_CommonStockId_FinancialConceptId_Unit_PeriodS~" ON public."FinancialFact" USING btree ("CommonStockId", "FinancialConceptId", "Unit", "PeriodStart", "PeriodEnd", "AccessionNumber", "DimensionsKey")'),
            ('FinancialFact', 'CommonStockId', 'pg_class', 'IX_FinancialFact_CommonStockId_FiscalYear_FiscalPeriod', 'CREATE INDEX "IX_FinancialFact_CommonStockId_FiscalYear_FiscalPeriod" ON public."FinancialFact" USING btree ("CommonStockId", "FiscalYear", "FiscalPeriod")'),
            ('FinancialFact', 'CommonStockId', 'pg_constraint', 'CK_FinancialFact_CanonicalOwnerMirror', 'CHECK ((NOT ("EquityIssuerId" IS DISTINCT FROM "CommonStockId")))'),
            ('FinancialFact', 'CommonStockId', 'pg_constraint', 'FK_FinancialFact_EquityIssuer_CommonStockId', 'FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT'),
            ('FinancialFact', 'CommonStockId', 'pg_constraint', 'FinancialFact_CommonStockId_not_null', 'NOT NULL "CommonStockId"'),
            ('FinancialFactsSyncStatus', 'CommonStockId', 'pg_class', 'IX_FinancialFactsSyncStatus_CommonStockId', 'CREATE UNIQUE INDEX "IX_FinancialFactsSyncStatus_CommonStockId" ON public."FinancialFactsSyncStatus" USING btree ("CommonStockId")'),
            ('FinancialFactsSyncStatus', 'CommonStockId', 'pg_constraint', 'CK_FinancialFactsSyncStatus_CanonicalOwnerMirror', 'CHECK ((NOT ("EquityIssuerId" IS DISTINCT FROM "CommonStockId")))'),
            ('FinancialFactsSyncStatus', 'CommonStockId', 'pg_constraint', 'FK_FinancialFactsSyncStatus_EquityIssuer_CommonStockId', 'FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT'),
            ('FinancialFactsSyncStatus', 'CommonStockId', 'pg_constraint', 'FinancialFactsSyncStatus_CommonStockId_not_null', 'NOT NULL "CommonStockId"'),
            ('Form144Filing', 'CommonStockId', 'pg_class', 'IX_Form144Filing_CommonStockId_FilingDate', 'CREATE INDEX "IX_Form144Filing_CommonStockId_FilingDate" ON public."Form144Filing" USING btree ("CommonStockId", "FilingDate")'),
            ('Form144Filing', 'CommonStockId', 'pg_constraint', 'CK_Form144Filing_CanonicalOwnerMirror', 'CHECK ((NOT ("EquityIssuerId" IS DISTINCT FROM "CommonStockId")))'),
            ('Form144Filing', 'CommonStockId', 'pg_constraint', 'FK_Form144Filing_EquityIssuer_CommonStockId', 'FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT'),
            ('Form144Filing', 'CommonStockId', 'pg_constraint', 'Form144Filing_CommonStockId_not_null', 'NOT NULL "CommonStockId"'),
            ('FormDFiling', 'CommonStockId', 'pg_class', 'IX_FormDFiling_CommonStockId_FilingDate', 'CREATE INDEX "IX_FormDFiling_CommonStockId_FilingDate" ON public."FormDFiling" USING btree ("CommonStockId", "FilingDate")'),
            ('FormDFiling', 'CommonStockId', 'pg_constraint', 'CK_FormDFiling_CanonicalOwnerMirror', 'CHECK ((NOT ("EquityIssuerId" IS DISTINCT FROM "CommonStockId")))'),
            ('FormDFiling', 'CommonStockId', 'pg_constraint', 'FK_FormDFiling_EquityIssuer_CommonStockId', 'FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT'),
            ('FormDFiling', 'CommonStockId', 'pg_constraint', 'FormDFiling_CommonStockId_not_null', 'NOT NULL "CommonStockId"'),
            ('FundSeries', 'CommonStockId', 'pg_class', 'IX_FundSeries_CommonStockId', 'CREATE INDEX "IX_FundSeries_CommonStockId" ON public."FundSeries" USING btree ("CommonStockId")'),
            ('FundSeries', 'CommonStockId', 'pg_constraint', 'CK_FundSeries_CanonicalOwnerMirror', 'CHECK ((NOT ("EquityIssuerId" IS DISTINCT FROM "CommonStockId")))'),
            ('FundSeries', 'CommonStockId', 'pg_constraint', 'FK_FundSeries_EquityIssuer_CommonStockId', 'FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT'),
            ('GovernmentContract', 'CommonStockId', 'pg_class', 'IX_GovernmentContract_CommonStockId_ActionDate', 'CREATE INDEX "IX_GovernmentContract_CommonStockId_ActionDate" ON public."GovernmentContract" USING btree ("CommonStockId", "ActionDate")'),
            ('GovernmentContract', 'CommonStockId', 'pg_constraint', 'CK_GovernmentContract_CanonicalOwnerMirror', 'CHECK ((NOT ("EquityIssuerId" IS DISTINCT FROM "CommonStockId")))'),
            ('GovernmentContract', 'CommonStockId', 'pg_constraint', 'FK_GovernmentContract_EquityIssuer_CommonStockId', 'FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT'),
            ('GovernmentContract', 'CommonStockId', 'pg_constraint', 'GovernmentContract_CommonStockId_not_null', 'NOT NULL "CommonStockId"'),
            ('InsiderTransaction', 'CommonStockId', 'pg_class', 'IX_InsiderTransaction_CommonStockId_TransactionDate', 'CREATE INDEX "IX_InsiderTransaction_CommonStockId_TransactionDate" ON public."InsiderTransaction" USING btree ("CommonStockId", "TransactionDate")'),
            ('InsiderTransaction', 'CommonStockId', 'pg_class', 'IX_InsiderTransaction_TransactionDate_Covering', 'CREATE INDEX "IX_InsiderTransaction_TransactionDate_Covering" ON public."InsiderTransaction" USING btree ("TransactionDate") INCLUDE ("Shares", "PricePerShare", "IsPriceValid", "SecurityKind", "SecurityTitle", "CommonStockId", "InsiderOwnerId", "TransactionCode", "IsRule10b5One")'),
            ('InsiderTransaction', 'CommonStockId', 'pg_constraint', 'CK_InsiderTransaction_CanonicalOwnerMirror', 'CHECK ((NOT ("EquityIssuerId" IS DISTINCT FROM "CommonStockId")))'),
            ('InsiderTransaction', 'CommonStockId', 'pg_constraint', 'FK_InsiderTransaction_EquityIssuer_CommonStockId', 'FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT'),
            ('InsiderTransaction', 'CommonStockId', 'pg_constraint', 'InsiderTransaction_CommonStockId_not_null', 'NOT NULL "CommonStockId"'),
            ('InstitutionalHolding', 'CommonStockId', 'pg_class', 'IX_InstitutionalHolding_CommonStockId_FilingDate', 'CREATE INDEX "IX_InstitutionalHolding_CommonStockId_FilingDate" ON public."InstitutionalHolding" USING btree ("CommonStockId", "FilingDate") INCLUDE ("AccessionNumber", "InstitutionalHolderId")'),
            ('InstitutionalHolding', 'CommonStockId', 'pg_class', 'IX_InstitutionalHolding_CommonStockId_InstitutionalHolderId_Re~', 'CREATE UNIQUE INDEX "IX_InstitutionalHolding_CommonStockId_InstitutionalHolderId_Re~" ON public."InstitutionalHolding" USING btree ("CommonStockId", "InstitutionalHolderId", "ReportDate", "ShareType", "OptionType", "FilingType", "ListedTicker") NULLS NOT DISTINCT'),
            ('InstitutionalHolding', 'CommonStockId', 'pg_class', 'IX_InstitutionalHolding_InstitutionalHolderId_ReportDate', 'CREATE INDEX "IX_InstitutionalHolding_InstitutionalHolderId_ReportDate" ON public."InstitutionalHolding" USING btree ("InstitutionalHolderId", "ReportDate") INCLUDE ("CommonStockId", "Value", "Shares", "FilingDate", "FilingType")'),
            ('InstitutionalHolding', 'CommonStockId', 'pg_class', 'IX_InstitutionalHolding_ReportDate_CommonStockId_Institutional~', 'CREATE INDEX "IX_InstitutionalHolding_ReportDate_CommonStockId_Institutional~" ON public."InstitutionalHolding" USING btree ("ReportDate", "CommonStockId", "InstitutionalHolderId") INCLUDE ("Shares", "Value")'),
            ('InstitutionalHolding', 'CommonStockId', 'pg_class', 'IX_InstitutionalHolding_ReportDate_InstitutionalHolderId_Commo~', 'CREATE INDEX "IX_InstitutionalHolding_ReportDate_InstitutionalHolderId_Commo~" ON public."InstitutionalHolding" USING btree ("ReportDate", "InstitutionalHolderId", "CommonStockId") INCLUDE ("Shares", "Value")'),
            ('InstitutionalHolding', 'CommonStockId', 'pg_class', 'IX_InstitutionalHolding_StockQuarterCommonValue', 'CREATE INDEX "IX_InstitutionalHolding_StockQuarterCommonValue" ON public."InstitutionalHolding" USING btree ("CommonStockId", "ReportDate", "InstitutionalHolderId") INCLUDE ("Value") WHERE (("FilingType" = 0) AND ("OptionType" IS NULL))'),
            ('InstitutionalHolding', 'CommonStockId', 'pg_class', 'IX_InstitutionalHolding_StockQuarterExposure', 'CREATE INDEX "IX_InstitutionalHolding_StockQuarterExposure" ON public."InstitutionalHolding" USING btree ("CommonStockId", "ReportDate") INCLUDE ("InstitutionalHolderId", "Value", "Shares", "ListedTicker", "FilingType", "OptionType")'),
            ('InstitutionalHolding', 'CommonStockId', 'pg_class', 'IX_InstitutionalHolding_ValuePending_Pairs', 'CREATE INDEX "IX_InstitutionalHolding_ValuePending_Pairs" ON public."InstitutionalHolding" USING btree ("CommonStockId", "ListedTicker", "ReportDate") WHERE "ValuePending"'),
            ('InstitutionalHolding', 'CommonStockId', 'pg_constraint', 'CK_InstitutionalHolding_CanonicalOwnerMirror', 'CHECK ((NOT ("EquityIssuerId" IS DISTINCT FROM "CommonStockId")))'),
            ('InstitutionalHolding', 'CommonStockId', 'pg_constraint', 'FK_InstitutionalHolding_EquityIssuer_CommonStockId', 'FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT'),
            ('InstitutionalHolding', 'CommonStockId', 'pg_constraint', 'InstitutionalHolding_CommonStockId_not_null', 'NOT NULL "CommonStockId"'),
            ('NCenFiling', 'CommonStockId', 'pg_class', 'IX_NCenFiling_CommonStockId_FilingDate', 'CREATE INDEX "IX_NCenFiling_CommonStockId_FilingDate" ON public."NCenFiling" USING btree ("CommonStockId", "FilingDate")'),
            ('NCenFiling', 'CommonStockId', 'pg_constraint', 'CK_NCenFiling_CanonicalOwnerMirror', 'CHECK ((NOT ("EquityIssuerId" IS DISTINCT FROM "CommonStockId")))'),
            ('NCenFiling', 'CommonStockId', 'pg_constraint', 'FK_NCenFiling_EquityIssuer_CommonStockId', 'FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT'),
            ('NCenFiling', 'CommonStockId', 'pg_constraint', 'NCenFiling_CommonStockId_not_null', 'NOT NULL "CommonStockId"'),
            ('NportFiling', 'CommonStockId', 'pg_class', 'IX_NportFiling_CommonStockId_FilingDate', 'CREATE INDEX "IX_NportFiling_CommonStockId_FilingDate" ON public."NportFiling" USING btree ("CommonStockId", "FilingDate")'),
            ('NportFiling', 'CommonStockId', 'pg_constraint', 'CK_NportFiling_CanonicalOwnerMirror', 'CHECK ((NOT ("EquityIssuerId" IS DISTINCT FROM "CommonStockId")))'),
            ('NportFiling', 'CommonStockId', 'pg_constraint', 'FK_NportFiling_EquityIssuer_CommonStockId', 'FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT'),
            ('OffExchangeVolume', 'CommonStockId', 'pg_class', 'IX_OffExchangeVolume_CommonStockId_ListedTicker_WeekStartDate', 'CREATE UNIQUE INDEX "IX_OffExchangeVolume_CommonStockId_ListedTicker_WeekStartDate" ON public."OffExchangeVolume" USING btree ("CommonStockId", "ListedTicker", "WeekStartDate")'),
            ('ReportedFinancialStatement', 'CommonStockId', 'pg_class', 'IX_ReportedFinancialStatement_CommonStockId_Kind_FiscalYear_Fi~', 'CREATE INDEX "IX_ReportedFinancialStatement_CommonStockId_Kind_FiscalYear_Fi~" ON public."ReportedFinancialStatement" USING btree ("CommonStockId", "Kind", "FiscalYear", "FiscalPeriod")'),
            ('ReportedFinancialStatement', 'CommonStockId', 'pg_constraint', 'CK_ReportedFinancialStatement_CanonicalOwnerMirror', 'CHECK ((NOT ("EquityIssuerId" IS DISTINCT FROM "CommonStockId")))'),
            ('ReportedFinancialStatement', 'CommonStockId', 'pg_constraint', 'FK_ReportedFinancialStatement_EquityIssuer_CommonStockId', 'FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT'),
            ('ReportedFinancialStatement', 'CommonStockId', 'pg_constraint', 'ReportedFinancialStatement_CommonStockId_not_null', 'NOT NULL "CommonStockId"'),
            ('ShortInterest', 'CommonStockId', 'pg_class', 'IX_ShortInterest_CommonStockId_ListedTicker_SettlementDate', 'CREATE UNIQUE INDEX "IX_ShortInterest_CommonStockId_ListedTicker_SettlementDate" ON public."ShortInterest" USING btree ("CommonStockId", "ListedTicker", "SettlementDate")'),
            ('StockQuarterlyActivity', 'CommonStockId', 'pg_constraint', 'CK_StockQuarterlyActivity_CanonicalOwnerMirror', 'CHECK ((NOT ("EquityIssuerId" IS DISTINCT FROM "CommonStockId")))'),
            ('StockQuarterlyActivity', 'CommonStockId', 'pg_constraint', 'FK_StockQuarterlyActivity_EquityIssuer_CommonStockId', 'FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT'),
            ('StockQuarterlyActivity', 'CommonStockId', 'pg_constraint', 'PK_StockQuarterlyActivity', 'PRIMARY KEY ("CommonStockId", "ReportDate")'),
            ('StockQuarterlyActivity', 'CommonStockId', 'pg_constraint', 'StockQuarterlyActivity_CommonStockId_not_null', 'NOT NULL "CommonStockId"'),
            ('StockQuarterlyActivityCombined', 'CommonStockId', 'pg_constraint', 'CK_StockQuarterlyActivityCombined_CanonicalOwnerMirror', 'CHECK ((NOT ("EquityIssuerId" IS DISTINCT FROM "CommonStockId")))'),
            ('StockQuarterlyActivityCombined', 'CommonStockId', 'pg_constraint', 'FK_StockQuarterlyActivityCombined_EquityIssuer_CommonStockId', 'FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT'),
            ('StockQuarterlyActivityCombined', 'CommonStockId', 'pg_constraint', 'PK_StockQuarterlyActivityCombined', 'PRIMARY KEY ("CommonStockId", "ReportDate")'),
            ('StockQuarterlyActivityCombined', 'CommonStockId', 'pg_constraint', 'StockQuarterlyActivityCombined_CommonStockId_not_null', 'NOT NULL "CommonStockId"'),
            ('StockQuarterlyListingActivity', 'CommonStockId', 'pg_constraint', 'CK_StockQuarterlyListingActivity_CanonicalOwnerMirror', 'CHECK ((NOT ("EquityIssuerId" IS DISTINCT FROM "CommonStockId")))'),
            ('StockQuarterlyListingActivity', 'CommonStockId', 'pg_constraint', 'FK_StockQuarterlyListingActivity_EquityIssuer_CommonStockId', 'FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT'),
            ('StockQuarterlyListingActivity', 'CommonStockId', 'pg_constraint', 'PK_StockQuarterlyListingActivity', 'PRIMARY KEY ("CommonStockId", "ReportDate", "IsCombined", "PriceSeriesTicker")'),
            ('StockQuarterlyListingActivity', 'CommonStockId', 'pg_constraint', 'StockQuarterlyListingActivity_CommonStockId_not_null', 'NOT NULL "CommonStockId"'),
            ('StockSplit', 'CommonStockId', 'pg_class', 'IX_StockSplit_CommonStockId_EffectiveDate', 'CREATE UNIQUE INDEX "IX_StockSplit_CommonStockId_EffectiveDate" ON public."StockSplit" USING btree ("CommonStockId", "EffectiveDate") WHERE (("PriceSeriesTicker" IS NULL) AND ("EquityListingId" IS NULL))'),
            ('StockSplit', 'CommonStockId', 'pg_class', 'IX_StockSplit_CommonStockId_PriceSeriesTicker_EffectiveDate', 'CREATE UNIQUE INDEX "IX_StockSplit_CommonStockId_PriceSeriesTicker_EffectiveDate" ON public."StockSplit" USING btree ("CommonStockId", "PriceSeriesTicker", "EffectiveDate") WHERE (("PriceSeriesTicker" IS NOT NULL) AND ("EquityListingId" IS NULL))'),
            ('StockSplit', 'CommonStockId', 'pg_constraint', 'CK_StockSplit_CanonicalOwnerMirror', 'CHECK ((NOT ("EquityIssuerId" IS DISTINCT FROM "CommonStockId")))'),
            ('StockSplit', 'CommonStockId', 'pg_constraint', 'FK_StockSplit_EquityIssuer_CommonStockId', 'FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT'),
            ('StockSplit', 'CommonStockId', 'pg_constraint', 'StockSplit_CommonStockId_not_null', 'NOT NULL "CommonStockId"'),
            ('TranscriptCheckStatuses', 'CommonStockId', 'pg_class', 'IX_TranscriptCheckStatuses_CommonStockId', 'CREATE UNIQUE INDEX "IX_TranscriptCheckStatuses_CommonStockId" ON public."TranscriptCheckStatuses" USING btree ("CommonStockId")'),
            ('TranscriptCheckStatuses', 'CommonStockId', 'pg_constraint', 'CK_TranscriptCheckStatuses_CanonicalOwnerMirror', 'CHECK ((NOT ("EquityIssuerId" IS DISTINCT FROM "CommonStockId")))'),
            ('TranscriptCheckStatuses', 'CommonStockId', 'pg_constraint', 'FK_TranscriptCheckStatuses_EquityIssuer_CommonStockId', 'FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT'),
            ('TranscriptCheckStatuses', 'CommonStockId', 'pg_constraint', 'TranscriptCheckStatuses_CommonStockId_not_null', 'NOT NULL "CommonStockId"')
    ), actual AS (
        SELECT DISTINCT t.relname::text AS table_name, a.attname::text AS column_name,
            d.classid::regclass::text AS object_catalog,
            CASE WHEN d.classid = 'pg_constraint'::regclass THEN c.conname::text ELSE i.relname::text END AS object_name,
            CASE WHEN d.classid = 'pg_constraint'::regclass THEN pg_get_constraintdef(c.oid) ELSE pg_get_indexdef(i.oid) END AS definition
        FROM pg_depend d JOIN pg_class t ON t.oid = d.refobjid
        JOIN pg_namespace n ON n.oid = t.relnamespace
        JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = d.refobjsubid
        JOIN dropped_columns target ON target.table_name = t.relname AND target.column_name = a.attname
        LEFT JOIN pg_constraint c ON d.classid = 'pg_constraint'::regclass AND c.oid = d.objid
        LEFT JOIN pg_class i ON d.classid = 'pg_class'::regclass AND i.oid = d.objid
        WHERE d.refclassid = 'pg_class'::regclass AND n.nspname = 'public'
          AND d.classid IN ('pg_class'::regclass, 'pg_constraint'::regclass)
          AND (d.classid <> 'pg_class'::regclass OR i.relkind = 'i')
    )
    SELECT string_agg(table_name || '.' || object_name, ', ' ORDER BY table_name, object_name)
        INTO unexpected FROM (SELECT * FROM actual EXCEPT SELECT * FROM approved) unreviewed;
    IF unexpected IS NOT NULL THEN
        RAISE EXCEPTION 'Unreviewed dependencies on retired identity columns: %', unexpected;
    END IF;
END $column_dependencies$;

DO $directory_graph$
BEGIN
    IF EXISTS (SELECT 1 FROM "LegacyEquityListing" mapping
        LEFT JOIN "EquityListing" listing ON listing."Id" = mapping."EquityListingId"
        LEFT JOIN "EquitySecurity" security ON security."Id" = listing."EquitySecurityId"
        WHERE listing."Id" IS NULL OR security."EquityIssuerId" IS DISTINCT FROM mapping."CommonStockId") THEN
        RAISE EXCEPTION 'Original listing mapping belongs to another native issuer';
    END IF;
    IF EXISTS (SELECT 1 FROM "CommonStock" original
        LEFT JOIN "EquityIssuer" issuer ON issuer."Id" = original."Id" WHERE issuer."Id" IS NULL) THEN
        RAISE EXCEPTION 'An original directory issuer has no native identity';
    END IF;
    IF EXISTS (SELECT 1 FROM "CommonStock" original
        CROSS JOIN LATERAL unnest(ARRAY[original."Ticker"] || original."SecondaryTickers" ||
            original."ReferenceTickers" || original."PriceHistoryBackfilledTickers") symbol
        LEFT JOIN "LegacyEquityListing" mapping
            ON mapping."CommonStockId" = original."Id" AND mapping."ListedTicker" = symbol
        WHERE nullif(symbol, '') IS NOT NULL AND mapping."EquityListingId" IS NULL) THEN
        RAISE EXCEPTION 'An original directory symbol has no retained listing identity';
    END IF;
END $directory_graph$;

-- Transaction-neutral audit body: caller must hold a transaction and stop source writes for retirement.
SET LOCAL timezone = 'UTC';
SET LOCAL extra_float_digits = 3;
DO $audit$
BEGIN
    IF EXISTS (
        SELECT 1 FROM "CommonStock" source
        WHERE NOT EXISTS (
            SELECT 1 FROM "EquityDirectorySourceRecord" evidence
            WHERE evidence."Source" = 'common-stock-v1'
              AND evidence."SourceRecordKey" = source."Id"::text
              AND evidence."PayloadJson" = to_jsonb(source)
              AND evidence."PayloadHash" = encode(sha256(convert_to(to_jsonb(source)::text, 'UTF8')), 'hex')
        )
    ) THEN
        RAISE EXCEPTION 'Original directory evidence is incomplete; source rows cannot be retired';
    END IF;
    IF EXISTS (
        SELECT 1 FROM "EquityDirectorySourceRecord"
        WHERE "Source" = 'common-stock-v1'
          AND ("PayloadJson"->>'Id' IS DISTINCT FROM "SourceRecordKey"
            OR "PayloadHash" <> encode(sha256(convert_to("PayloadJson"::text, 'UTF8')), 'hex'))
    ) THEN
        RAISE EXCEPTION 'Original directory evidence has invalid identity or content hashes';
    END IF;
END;
$audit$;
SELECT count(*) AS source_rows FROM "CommonStock";
SELECT count(*) AS preserved_versions, count(DISTINCT "SourceRecordKey") AS preserved_source_ids
FROM "EquityDirectorySourceRecord" WHERE "Source" = 'common-stock-v1';

-- Audit body only: the caller owns the transaction and source locks at final retirement.
-- Preserve exact original composite keys and every stored cursor field, including timestamps.
DO $audit$
DECLARE missing bigint; invalid bigint;
BEGIN
    SET LOCAL timezone = 'UTC';
    SET LOCAL extra_float_digits = 3;
    SELECT count(*) INTO missing FROM "LegacyEquityListing" r
    WHERE NOT EXISTS (
        SELECT 1 FROM "EquityDirectorySourceRecord" e
        WHERE e."Source" = 'legacy-equity-listing-v1'
          AND e."SourceRecordKey" = jsonb_build_array(r."CommonStockId", r."ListedTicker")::text
          AND e."PayloadJson" = to_jsonb(r)
          AND e."PayloadHash" = encode(sha256(convert_to(to_jsonb(r)::text, 'UTF8')), 'hex')
    );
    IF missing <> 0 THEN RAISE EXCEPTION '% original listing mappings are not preserved', missing; END IF;

    SELECT count(*) INTO missing FROM "CorporateActionPriceReconciliationCursor" r
    WHERE NOT EXISTS (
        SELECT 1 FROM "EquityDirectorySourceRecord" e
        WHERE e."Source" = 'corporate-action-cursor-v1' AND e."SourceRecordKey" = r."Name"
          AND e."PayloadJson" = to_jsonb(r)
          AND e."PayloadHash" = encode(sha256(convert_to(to_jsonb(r)::text, 'UTF8')), 'hex')
    );
    IF missing <> 0 THEN RAISE EXCEPTION '% original cursor rows are not preserved', missing; END IF;

    SELECT count(*) INTO invalid FROM "EquityDirectorySourceRecord" e
    WHERE e."Source" IN ('legacy-equity-listing-v1', 'corporate-action-cursor-v1')
      AND (e."PayloadHash" IS DISTINCT FROM encode(sha256(convert_to(e."PayloadJson"::text, 'UTF8')), 'hex')
        OR e."SourceRecordKey" IS DISTINCT FROM CASE e."Source"
            WHEN 'legacy-equity-listing-v1' THEN jsonb_build_array(e."PayloadJson"->>'CommonStockId', e."PayloadJson"->>'ListedTicker')::text
            ELSE e."PayloadJson"->>'Name' END);
    IF invalid <> 0 THEN RAISE EXCEPTION '% retained identity source hashes or keys are invalid', invalid; END IF;
END;
$audit$;

SELECT "Source", count(*) AS preserved_versions,
    count(DISTINCT "SourceRecordKey") AS preserved_original_keys
FROM "EquityDirectorySourceRecord"
WHERE "Source" IN ('legacy-equity-listing-v1', 'corporate-action-cursor-v1')
GROUP BY "Source" ORDER BY "Source";

-- Exact row equivalence during the finite compatibility window; no database writes.
DO $verify$ BEGIN
    IF EXISTS (SELECT 1 FROM "CommonStockCusipAlias" WHERE "CommonStockId" IS DISTINCT FROM "EquityIssuerId") THEN
        RAISE EXCEPTION 'Owner mismatch: CommonStockCusipAlias'; END IF;
    IF EXISTS (
        (SELECT to_jsonb(row) - 'CommonStockId' FROM "CommonStockCusipAlias" row EXCEPT ALL SELECT to_jsonb(row) FROM "EquityIssuerCusipAlias" row)
        UNION ALL
        (SELECT to_jsonb(row) FROM "EquityIssuerCusipAlias" row EXCEPT ALL SELECT to_jsonb(row) - 'CommonStockId' FROM "CommonStockCusipAlias" row)
    ) THEN RAISE EXCEPTION 'Evidence mismatch: CommonStockCusipAlias / EquityIssuerCusipAlias'; END IF;
    IF EXISTS (SELECT 1 FROM "CommonStockTickerAlias" WHERE "CommonStockId" IS DISTINCT FROM "EquityIssuerId") THEN
        RAISE EXCEPTION 'Owner mismatch: CommonStockTickerAlias'; END IF;
    IF EXISTS (
        (SELECT to_jsonb(row) - 'CommonStockId' FROM "CommonStockTickerAlias" row EXCEPT ALL SELECT to_jsonb(row) FROM "EquityIssuerTickerAlias" row)
        UNION ALL
        (SELECT to_jsonb(row) FROM "EquityIssuerTickerAlias" row EXCEPT ALL SELECT to_jsonb(row) - 'CommonStockId' FROM "CommonStockTickerAlias" row)
    ) THEN RAISE EXCEPTION 'Evidence mismatch: CommonStockTickerAlias / EquityIssuerTickerAlias'; END IF;
    IF EXISTS (SELECT 1 FROM "CommonStockTickerEvidence" WHERE "CommonStockId" IS DISTINCT FROM "EquityIssuerId") THEN
        RAISE EXCEPTION 'Owner mismatch: CommonStockTickerEvidence'; END IF;
    IF EXISTS (
        (SELECT to_jsonb(row) - 'CommonStockId' FROM "CommonStockTickerEvidence" row EXCEPT ALL SELECT to_jsonb(row) FROM "EquityIssuerTickerEvidence" row)
        UNION ALL
        (SELECT to_jsonb(row) FROM "EquityIssuerTickerEvidence" row EXCEPT ALL SELECT to_jsonb(row) - 'CommonStockId' FROM "CommonStockTickerEvidence" row)
    ) THEN RAISE EXCEPTION 'Evidence mismatch: CommonStockTickerEvidence / EquityIssuerTickerEvidence'; END IF;
    IF EXISTS (SELECT 1 FROM "CommonStockListedCusip" WHERE "CommonStockId" IS DISTINCT FROM "EquityIssuerId") THEN
        RAISE EXCEPTION 'Owner mismatch: CommonStockListedCusip'; END IF;
    IF EXISTS (
        (SELECT to_jsonb(row) - 'CommonStockId' FROM "CommonStockListedCusip" row EXCEPT ALL SELECT to_jsonb(row) FROM "EquityListingCusipEvidence" row)
        UNION ALL
        (SELECT to_jsonb(row) FROM "EquityListingCusipEvidence" row EXCEPT ALL SELECT to_jsonb(row) - 'CommonStockId' FROM "CommonStockListedCusip" row)
    ) THEN RAISE EXCEPTION 'Evidence mismatch: CommonStockListedCusip / EquityListingCusipEvidence'; END IF;
    IF EXISTS (SELECT 1 FROM "CommonStockDelistedListing" WHERE "CommonStockId" IS DISTINCT FROM "EquityIssuerId") THEN
        RAISE EXCEPTION 'Owner mismatch: CommonStockDelistedListing'; END IF;
    IF EXISTS (
        (SELECT to_jsonb(row) - 'CommonStockId' FROM "CommonStockDelistedListing" row EXCEPT ALL SELECT to_jsonb(row) FROM "EquityListingRetirementEvidence" row)
        UNION ALL
        (SELECT to_jsonb(row) FROM "EquityListingRetirementEvidence" row EXCEPT ALL SELECT to_jsonb(row) - 'CommonStockId' FROM "CommonStockDelistedListing" row)
    ) THEN RAISE EXCEPTION 'Evidence mismatch: CommonStockDelistedListing / EquityListingRetirementEvidence'; END IF;
    IF EXISTS (SELECT 1 FROM "ListedSecurity" WHERE "CommonStockId" IS DISTINCT FROM "EquityIssuerId") THEN
        RAISE EXCEPTION 'Owner mismatch: ListedSecurity'; END IF;
    IF EXISTS (
        (SELECT to_jsonb(row) - 'CommonStockId' FROM "ListedSecurity" row EXCEPT ALL SELECT to_jsonb(row) FROM "IssuerSecurityRegistration" row)
        UNION ALL
        (SELECT to_jsonb(row) FROM "IssuerSecurityRegistration" row EXCEPT ALL SELECT to_jsonb(row) - 'CommonStockId' FROM "ListedSecurity" row)
    ) THEN RAISE EXCEPTION 'Evidence mismatch: ListedSecurity / IssuerSecurityRegistration'; END IF;
END $verify$;

DO $prices$
BEGIN
    IF EXISTS (SELECT 1 FROM "DailyStockPrice" original
        LEFT JOIN "UnattributedDailyStockPrice" native ON native."Id" = original."Id"
        WHERE to_jsonb(original) IS DISTINCT FROM
            ((to_jsonb(native) - 'EquityIssuerId') || jsonb_build_object('CommonStockId', native."EquityIssuerId"))) THEN
        RAISE EXCEPTION 'An original unattributed price is missing or changed';
    END IF;
    IF EXISTS (SELECT 1 FROM "ListedDailyStockPrice" original
        LEFT JOIN "EquityDailyStockPrice" native ON native."Id" = original."Id"
        LEFT JOIN "LegacyEquityListing" mapping ON mapping."EquityListingId" = native."EquityListingId"
        WHERE to_jsonb(original) IS DISTINCT FROM
            ((to_jsonb(native) - 'EquityListingId' - 'SourceTicker') ||
                jsonb_build_object('CommonStockId', mapping."CommonStockId", 'ListedTicker', mapping."ListedTicker"))) THEN
        RAISE EXCEPTION 'An original listed price is missing or changed';
    END IF;
END $prices$;

-- Compare every original row, including retained rows whose old directory owner was removed.
-- Native histories without either counterpart require the immutable pre-cutover export.
DO $audit$
BEGIN
    IF to_regclass('"EquityListingTickerAlias"') IS NULL THEN
        IF EXISTS (SELECT 1 FROM "EquityDailyStockPrice" p
            JOIN "LegacyEquityListing" mapping ON mapping."EquityListingId" = p."EquityListingId"
            WHERE p."SourceTicker" IS DISTINCT FROM mapping."ListedTicker") THEN
            RAISE EXCEPTION 'Initial native price source differs from its original series key';
        END IF;
    ELSE
    IF EXISTS (
        SELECT 1 FROM "EquityDailyStockPrice" p
        JOIN "LegacyEquityListing" mapping ON mapping."EquityListingId" = p."EquityListingId"
        JOIN "EquityListing" listing ON listing."Id" = p."EquityListingId"
        WHERE p."SourceTicker" IS DISTINCT FROM mapping."ListedTicker"
          AND p."SourceTicker" IS DISTINCT FROM listing."Ticker"
          AND NOT EXISTS (SELECT 1 FROM "EquityListingTickerAlias" alias
              WHERE alias."EquityListingId" = p."EquityListingId" AND alias."Ticker" = p."SourceTicker")
    ) THEN
        RAISE EXCEPTION 'Native price source symbol has no retained listing identity';
    END IF;
    END IF;
    IF EXISTS ((SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", p."CommonStockId" FROM "DailyStockPrice" p) EXCEPT ALL (SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", p."EquityIssuerId" FROM "UnattributedDailyStockPrice" p WHERE EXISTS (SELECT 1 FROM "CommonStock" owner WHERE owner."Id" = p."EquityIssuerId") OR EXISTS (SELECT 1 FROM "DailyStockPrice" original WHERE original."Id" = p."Id")))
       OR EXISTS ((SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", p."EquityIssuerId" FROM "UnattributedDailyStockPrice" p WHERE EXISTS (SELECT 1 FROM "CommonStock" owner WHERE owner."Id" = p."EquityIssuerId") OR EXISTS (SELECT 1 FROM "DailyStockPrice" original WHERE original."Id" = p."Id")) EXCEPT ALL (SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", p."CommonStockId" FROM "DailyStockPrice" p)) THEN
        RAISE EXCEPTION 'DailyStockPrice and UnattributedDailyStockPrice differ; native price conservation failed';
    END IF;
    IF EXISTS ((SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", p."CommonStockId", p."ListedTicker" FROM "ListedDailyStockPrice" p) EXCEPT ALL (SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", m."CommonStockId", m."ListedTicker" FROM "EquityDailyStockPrice" p JOIN "LegacyEquityListing" m ON m."EquityListingId" = p."EquityListingId" WHERE EXISTS (SELECT 1 FROM "CommonStock" owner WHERE owner."Id" = m."CommonStockId") OR EXISTS (SELECT 1 FROM "ListedDailyStockPrice" original WHERE original."Id" = p."Id")))
       OR EXISTS ((SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", m."CommonStockId", m."ListedTicker" FROM "EquityDailyStockPrice" p JOIN "LegacyEquityListing" m ON m."EquityListingId" = p."EquityListingId" WHERE EXISTS (SELECT 1 FROM "CommonStock" owner WHERE owner."Id" = m."CommonStockId") OR EXISTS (SELECT 1 FROM "ListedDailyStockPrice" original WHERE original."Id" = p."Id")) EXCEPT ALL (SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", p."CommonStockId", p."ListedTicker" FROM "ListedDailyStockPrice" p)) THEN
        RAISE EXCEPTION 'ListedDailyStockPrice and EquityDailyStockPrice differ; native price conservation failed';
    END IF;
END;
$audit$;
SELECT 'retained_exact_without_legacy_owner' AS cohort, count(*) AS rows
FROM "EquityDailyStockPrice" p
JOIN "LegacyEquityListing" mapping ON mapping."EquityListingId" = p."EquityListingId"
WHERE NOT EXISTS (SELECT 1 FROM "CommonStock" owner WHERE owner."Id" = mapping."CommonStockId")
UNION ALL
SELECT 'retained_unattributed_without_legacy_owner', count(*)
FROM "UnattributedDailyStockPrice" p
WHERE NOT EXISTS (SELECT 1 FROM "CommonStock" owner WHERE owner."Id" = p."EquityIssuerId");

DO $observations$
DECLARE target text; invalid boolean;
BEGIN
    FOREACH target IN ARRAY ARRAY['FailToDeliver', 'DailyShortVolume', 'ShortInterest', 'OffExchangeVolume'] LOOP
        EXECUTE format('SELECT EXISTS (SELECT 1 FROM %I observation
            LEFT JOIN "EquityListing" listing ON listing."Id" = observation."EquityListingId"
            LEFT JOIN "EquitySecurity" security ON security."Id" = listing."EquitySecurityId"
            LEFT JOIN "LegacyEquityListing" mapping ON mapping."EquityListingId" = listing."Id"
            LEFT JOIN "LegacyEquityListing" original_mapping ON original_mapping."CommonStockId" = observation."CommonStockId"
                AND original_mapping."ListedTicker" = observation."ListedTicker"
            WHERE listing."Id" IS NULL OR (observation."CommonStockId" IS NOT NULL AND
                (observation."CommonStockId" IS DISTINCT FROM security."EquityIssuerId" OR
                 observation."CommonStockId" IS DISTINCT FROM mapping."CommonStockId" OR
                 CASE WHEN original_mapping."EquityListingId" IS NOT NULL
                    THEN original_mapping."EquityListingId" IS DISTINCT FROM observation."EquityListingId"
                    ELSE ARRAY(SELECT DISTINCT candidate."Id" FROM "EquityListing" candidate
                        JOIN "EquitySecurity" candidate_security ON candidate_security."Id" = candidate."EquitySecurityId"
                        WHERE candidate_security."EquityIssuerId" = observation."CommonStockId" AND candidate."MarketCountryCode" = ''US''
                          AND (candidate."Ticker" = observation."ListedTicker" OR EXISTS (SELECT 1 FROM "EquityListingTickerAlias" alias
                              WHERE alias."EquityListingId" = candidate."Id" AND alias."Ticker" = observation."ListedTicker")))
                        IS DISTINCT FROM ARRAY[observation."EquityListingId"] END)))', target) INTO invalid;
        IF invalid THEN RAISE EXCEPTION 'Original listing ownership differs in %', target; END IF;
    END LOOP;
END $observations$;

DO $triggers$
DECLARE target record;
BEGIN
    FOR target IN
        SELECT n.nspname AS schema_name, c.relname AS table_name, t.tgname AS trigger_name
        FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_proc p ON p.oid = t.tgfoid JOIN pg_namespace pn ON pn.oid = p.pronamespace
        WHERE NOT t.tgisinternal AND n.nspname = 'public' AND pn.nspname = 'public'
          AND p.proname = ANY(ARRAY['eq_bridge_finra_listing', 'eq_bridge_ftd_listing', 'eq_capture_legacy_listing_symbol', 'eq_capture_original_directory_record', 'eq_capture_retiring_identity_source', 'eq_ensure_legacy_listing', 'eq_guard_unmigrated_directory_history', 'eq_mirror_canonical_owner_commonstockid', 'eq_mirror_evidence_commonstockcusipalias', 'eq_mirror_evidence_commonstockdelistedlisting', 'eq_mirror_evidence_commonstocklistedcusip', 'eq_mirror_evidence_commonstocktickeralias', 'eq_mirror_evidence_commonstocktickerevidence', 'eq_mirror_evidence_equityissuercusipalias', 'eq_mirror_evidence_equityissuertickeralias', 'eq_mirror_evidence_equityissuertickerevidence', 'eq_mirror_evidence_equitylistingcusipevidence', 'eq_mirror_evidence_equitylistingretirementevidence', 'eq_mirror_evidence_issuersecurityregistration', 'eq_mirror_evidence_listedsecurity', 'eq_record_original_directory_change', 'eq_record_retiring_cursor_identity', 'eq_record_retiring_listing_identity', 'eq_refresh_native_profile', 'eq_set_legacy_listing_market', 'eq_sync_legacy_issuer', 'eq_sync_legacy_series', 'eq_sync_native_daily_price', 'eq_sync_native_profile', 'eq_sync_retiring_daily_price'])
    LOOP
        EXECUTE format('DROP TRIGGER %I ON %I.%I', target.trigger_name, target.schema_name, target.table_name);
    END LOOP;
END $triggers$;

DROP FUNCTION public."eq_bridge_finra_listing"();

DROP FUNCTION public."eq_bridge_ftd_listing"();

DROP FUNCTION public."eq_capture_legacy_listing_symbol"();

DROP FUNCTION public."eq_capture_original_directory_record"(source_row jsonb);

DROP FUNCTION public."eq_capture_retiring_identity_source"(source_kind text, source_key text, source_row jsonb);

DROP FUNCTION public."eq_ensure_legacy_listing"(owner_id uuid, symbol text, protect_concurrent_writers boolean);

DROP FUNCTION public."eq_guard_unmigrated_directory_history"();

DROP FUNCTION public."eq_mirror_canonical_owner_commonstockid"();

DROP FUNCTION public."eq_mirror_evidence_commonstockcusipalias"();

DROP FUNCTION public."eq_mirror_evidence_commonstockdelistedlisting"();

DROP FUNCTION public."eq_mirror_evidence_commonstocklistedcusip"();

DROP FUNCTION public."eq_mirror_evidence_commonstocktickeralias"();

DROP FUNCTION public."eq_mirror_evidence_commonstocktickerevidence"();

DROP FUNCTION public."eq_mirror_evidence_equityissuercusipalias"();

DROP FUNCTION public."eq_mirror_evidence_equityissuertickeralias"();

DROP FUNCTION public."eq_mirror_evidence_equityissuertickerevidence"();

DROP FUNCTION public."eq_mirror_evidence_equitylistingcusipevidence"();

DROP FUNCTION public."eq_mirror_evidence_equitylistingretirementevidence"();

DROP FUNCTION public."eq_mirror_evidence_issuersecurityregistration"();

DROP FUNCTION public."eq_mirror_evidence_listedsecurity"();

DROP FUNCTION public."eq_record_original_directory_change"();

DROP FUNCTION public."eq_record_retiring_cursor_identity"();

DROP FUNCTION public."eq_record_retiring_listing_identity"();

DROP FUNCTION public."eq_refresh_native_profile"(owner_id uuid);

DROP FUNCTION public."eq_set_legacy_listing_market"();

DROP FUNCTION public."eq_sync_legacy_issuer"();

DROP FUNCTION public."eq_sync_legacy_series"();

DROP FUNCTION public."eq_sync_native_daily_price"();

DROP FUNCTION public."eq_sync_native_profile"();

DROP FUNCTION public."eq_sync_retiring_daily_price"();

ALTER TABLE "CongressionalTrade" DROP COLUMN "CommonStockId";

ALTER TABLE "CashDividend" DROP COLUMN "CommonStockId";

ALTER TABLE "StockSplit" DROP COLUMN "CommonStockId";

ALTER TABLE "FdaCatalyst" DROP COLUMN "CommonStockId";

ALTER TABLE "GovernmentContract" DROP COLUMN "CommonStockId";

ALTER TABLE "InstitutionalHolding" DROP COLUMN "CommonStockId";

ALTER TABLE "StockQuarterlyActivity" DROP CONSTRAINT "PK_StockQuarterlyActivity";
ALTER TABLE "StockQuarterlyActivity" ADD CONSTRAINT "PK_StockQuarterlyActivity" PRIMARY KEY USING INDEX "UX_StockQuarterlyActivity_CanonicalOwnerKey";

ALTER TABLE "StockQuarterlyActivity" DROP COLUMN "CommonStockId";

ALTER TABLE "StockQuarterlyActivityCombined" DROP CONSTRAINT "PK_StockQuarterlyActivityCombined";
ALTER TABLE "StockQuarterlyActivityCombined" ADD CONSTRAINT "PK_StockQuarterlyActivityCombined" PRIMARY KEY USING INDEX "UX_StockQuarterlyActivityCombined_CanonicalOwnerKey";

ALTER TABLE "StockQuarterlyActivityCombined" DROP COLUMN "CommonStockId";

ALTER TABLE "StockQuarterlyListingActivity" DROP CONSTRAINT "PK_StockQuarterlyListingActivity";
ALTER TABLE "StockQuarterlyListingActivity" ADD CONSTRAINT "PK_StockQuarterlyListingActivity" PRIMARY KEY USING INDEX "UX_StockQuarterlyListingActivity_CanonicalOwnerKey";

ALTER TABLE "StockQuarterlyListingActivity" DROP COLUMN "CommonStockId";

ALTER TABLE "Form144Filing" DROP COLUMN "CommonStockId";

ALTER TABLE "InsiderTransaction" DROP COLUMN "CommonStockId";

ALTER TABLE "CompanyFilingSyncState" DROP CONSTRAINT "PK_CompanyFilingSyncState";
ALTER TABLE "CompanyFilingSyncState" ADD CONSTRAINT "PK_CompanyFilingSyncState" PRIMARY KEY USING INDEX "UX_CompanyFilingSyncState_CanonicalOwnerKey";

ALTER TABLE "CompanyFilingSyncState" DROP COLUMN "CommonStockId";

ALTER TABLE "Document" DROP COLUMN "CommonStockId";

ALTER TABLE "FormDFiling" DROP COLUMN "CommonStockId";

ALTER TABLE "FundSeries" DROP COLUMN "CommonStockId";

ALTER TABLE "NCenFiling" DROP COLUMN "CommonStockId";

ALTER TABLE "NportFiling" DROP COLUMN "CommonStockId";

ALTER TABLE "TranscriptCheckStatuses" DROP COLUMN "CommonStockId";

ALTER TABLE "FinancialFact" DROP COLUMN "CommonStockId";

ALTER TABLE "FinancialFactsSyncStatus" DROP COLUMN "CommonStockId";

ALTER TABLE "ReportedFinancialStatement" DROP COLUMN "CommonStockId";

ALTER TABLE "FailToDeliver" DROP COLUMN "CommonStockId";

ALTER TABLE "DailyShortVolume" DROP COLUMN "CommonStockId";

ALTER TABLE "ShortInterest" DROP COLUMN "CommonStockId";

ALTER TABLE "OffExchangeVolume" DROP COLUMN "CommonStockId";

ALTER TABLE "EquityIssuer" DROP COLUMN "CommonStockId";

ALTER TABLE "CorporateActionPriceReconciliationCursor" DROP COLUMN "LastCommonStockId", DROP COLUMN "LastListedTicker";

DROP TABLE "CommonStock", "LegacyEquityListing", "DailyStockPrice", "ListedDailyStockPrice", "CommonStockCusipAlias", "CommonStockTickerAlias", "CommonStockTickerEvidence", "CommonStockListedCusip", "CommonStockDelistedListing", "ListedSecurity";

DROP TABLE "EquityOwnerMigrationProgress", "NativeListingObservationMigrationProgress", "NativePriceMigrationProgress";

ALTER INDEX "IX_CongressionalTrade_LegacyFilingIdentity_CanonicalOwner" RENAME TO "IX_CongressionalTrade_LegacyFilingIdentity";

ALTER INDEX "IX_InstitutionalHolding_InstitutionalHolderId_Report_0362a272bc" RENAME TO "IX_InstitutionalHolding_InstitutionalHolderId_ReportDate";

ALTER INDEX "IX_InstitutionalHolding_StockQuarterCommonValue_CanonicalOwner" RENAME TO "IX_InstitutionalHolding_StockQuarterCommonValue";

ALTER INDEX "IX_InstitutionalHolding_StockQuarterExposure_CanonicalOwner" RENAME TO "IX_InstitutionalHolding_StockQuarterExposure";

ALTER INDEX "IX_InstitutionalHolding_ValuePending_Pairs_CanonicalOwner" RENAME TO "IX_InstitutionalHolding_ValuePending_Pairs";

ALTER INDEX "IX_InsiderTransaction_TransactionDate_Covering_CanonicalOwner" RENAME TO "IX_InsiderTransaction_TransactionDate_Covering";

DO $complete$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND column_name IN ('CommonStockId', 'StockId', 'LastCommonStockId', 'LastListedTicker')) THEN
        RAISE EXCEPTION 'Retired identity columns remain outside the verified contract';
    END IF;
END $complete$;
