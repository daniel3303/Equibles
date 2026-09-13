-- Ownership equivalence only; compare original full-row exports separately before retirement.
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
DO $audit$
DECLARE target record; mismatches bigint;
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
        IF NOT EXISTS (SELECT 1 FROM "EquityOwnerMigrationProgress" WHERE "TableName" = target.table_name AND "Completed") THEN
            RAISE EXCEPTION 'Canonical owner backfill is incomplete for %', target.table_name;
        END IF;
        EXECUTE format('SELECT count(*) FROM %I WHERE "EquityIssuerId" IS DISTINCT FROM %I', target.table_name, target.previous_column)
            INTO mismatches;
        IF mismatches <> 0 THEN RAISE EXCEPTION '% has % differing owner references', target.table_name, mismatches; END IF;
    END LOOP;
    FOR target IN SELECT unnest(ARRAY['IX_CommonStockCusipAlias_EquityIssuerId', 'IX_CommonStockTickerAlias_EquityIssuerId', 'IX_CommonStockTickerEvidence_EquityIssuerId_Ticker_SourceDocum~', 'IX_CommonStockListedCusip_EquityIssuerId', 'IX_CommonStockDelistedListing_EquityIssuerId_ListedTicker', 'IX_CongressionalTrade_EquityIssuerId_TransactionDate', 'IX_CongressionalTrade_LegacyFilingIdentity_CanonicalOwner', 'IX_CashDividend_EquityIssuerId_ExDate', 'IX_StockSplit_EquityIssuerId_EffectiveDate', 'IX_StockSplit_EquityIssuerId_PriceSeriesTicker_EffectiveDate', 'IX_FdaCatalyst_EquityIssuerId', 'IX_GovernmentContract_EquityIssuerId_ActionDate', 'IX_InstitutionalHolding_EquityIssuerId_FilingDate', 'IX_InstitutionalHolding_EquityIssuerId_InstitutionalHolderId_R~', 'IX_InstitutionalHolding_InstitutionalHolderId_Report_0362a272bc', 'IX_InstitutionalHolding_ReportDate_EquityIssuerId_Institutiona~', 'IX_InstitutionalHolding_ReportDate_InstitutionalHolderId_Equit~', 'IX_InstitutionalHolding_StockQuarterCommonValue_CanonicalOwner', 'IX_InstitutionalHolding_StockQuarterExposure_CanonicalOwner', 'IX_InstitutionalHolding_ValuePending_Pairs_CanonicalOwner', 'UX_StockQuarterlyActivity_CanonicalOwnerKey', 'UX_StockQuarterlyActivityCombined_CanonicalOwnerKey', 'UX_StockQuarterlyListingActivity_CanonicalOwnerKey', 'IX_Form144Filing_EquityIssuerId_FilingDate', 'IX_InsiderTransaction_EquityIssuerId_TransactionDate', 'IX_InsiderTransaction_TransactionDate_Covering_CanonicalOwner', 'UX_CompanyFilingSyncState_CanonicalOwnerKey', 'IX_Document_EquityIssuerId_DocumentType', 'IX_FormDFiling_EquityIssuerId_FilingDate', 'IX_FundSeries_EquityIssuerId', 'IX_NCenFiling_EquityIssuerId_FilingDate', 'IX_NportFiling_EquityIssuerId_FilingDate', 'IX_TranscriptCheckStatuses_EquityIssuerId', 'IX_FinancialFact_EquityIssuerId_FinancialConceptId_PeriodEnd', 'IX_FinancialFact_EquityIssuerId_FinancialConceptId_Unit_Period~', 'IX_FinancialFact_EquityIssuerId_FiscalYear_FiscalPeriod', 'IX_FinancialFactsSyncStatus_EquityIssuerId', 'IX_ListedSecurity_EquityIssuerId_TradingSymbol', 'IX_ReportedFinancialStatement_EquityIssuerId_Kind_FiscalYear_F~']) AS index_name
    LOOP
        IF NOT EXISTS (SELECT 1 FROM pg_index WHERE indexrelid = to_regclass(format('%I', target.index_name)) AND indisvalid AND indisready) THEN
            RAISE EXCEPTION 'Canonical owner index % is unavailable', target.index_name;
        END IF;
    END LOOP;
    FOR target IN SELECT * FROM (VALUES
            ('CommonStockCusipAlias', 'FK_CommonStockCusipAlias_EquityIssuer_EquityIssuerId'),
            ('CommonStockCusipAlias', 'CK_CommonStockCusipAlias_CanonicalOwnerMirror'),
            ('CommonStockCusipAlias', 'CK_CommonStockCusipAlias_CanonicalOwnerNotNull'),
            ('CommonStockTickerAlias', 'FK_CommonStockTickerAlias_EquityIssuer_EquityIssuerId'),
            ('CommonStockTickerAlias', 'CK_CommonStockTickerAlias_CanonicalOwnerMirror'),
            ('CommonStockTickerAlias', 'CK_CommonStockTickerAlias_CanonicalOwnerNotNull'),
            ('CommonStockTickerEvidence', 'FK_CommonStockTickerEvidence_EquityIssuer_EquityIssuerId'),
            ('CommonStockTickerEvidence', 'CK_CommonStockTickerEvidence_CanonicalOwnerMirror'),
            ('CommonStockTickerEvidence', 'CK_CommonStockTickerEvidence_CanonicalOwnerNotNull'),
            ('CommonStockListedCusip', 'FK_CommonStockListedCusip_EquityIssuer_EquityIssuerId'),
            ('CommonStockListedCusip', 'CK_CommonStockListedCusip_CanonicalOwnerMirror'),
            ('CommonStockListedCusip', 'CK_CommonStockListedCusip_CanonicalOwnerNotNull'),
            ('CommonStockDelistedListing', 'FK_CommonStockDelistedListing_EquityIssuer_EquityIssuerId'),
            ('CommonStockDelistedListing', 'CK_CommonStockDelistedListing_CanonicalOwnerMirror'),
            ('CommonStockDelistedListing', 'CK_CommonStockDelistedListing_CanonicalOwnerNotNull'),
            ('CongressionalTrade', 'FK_CongressionalTrade_EquityIssuer_EquityIssuerId'),
            ('CongressionalTrade', 'CK_CongressionalTrade_CanonicalOwnerMirror'),
            ('CashDividend', 'FK_CashDividend_EquityIssuer_EquityIssuerId'),
            ('CashDividend', 'CK_CashDividend_CanonicalOwnerMirror'),
            ('CashDividend', 'CK_CashDividend_CanonicalOwnerNotNull'),
            ('StockSplit', 'FK_StockSplit_EquityIssuer_EquityIssuerId'),
            ('StockSplit', 'CK_StockSplit_CanonicalOwnerMirror'),
            ('StockSplit', 'CK_StockSplit_CanonicalOwnerNotNull'),
            ('FdaCatalyst', 'FK_FdaCatalyst_EquityIssuer_EquityIssuerId'),
            ('FdaCatalyst', 'CK_FdaCatalyst_CanonicalOwnerMirror'),
            ('GovernmentContract', 'FK_GovernmentContract_EquityIssuer_EquityIssuerId'),
            ('GovernmentContract', 'CK_GovernmentContract_CanonicalOwnerMirror'),
            ('GovernmentContract', 'CK_GovernmentContract_CanonicalOwnerNotNull'),
            ('InstitutionalHolding', 'FK_InstitutionalHolding_EquityIssuer_EquityIssuerId'),
            ('InstitutionalHolding', 'CK_InstitutionalHolding_CanonicalOwnerMirror'),
            ('InstitutionalHolding', 'CK_InstitutionalHolding_CanonicalOwnerNotNull'),
            ('StockQuarterlyActivity', 'FK_StockQuarterlyActivity_EquityIssuer_EquityIssuerId'),
            ('StockQuarterlyActivity', 'CK_StockQuarterlyActivity_CanonicalOwnerMirror'),
            ('StockQuarterlyActivity', 'CK_StockQuarterlyActivity_CanonicalOwnerNotNull'),
            ('StockQuarterlyActivityCombined', 'FK_StockQuarterlyActivityCombined_EquityIssuer_EquityIssuerId'),
            ('StockQuarterlyActivityCombined', 'CK_StockQuarterlyActivityCombined_CanonicalOwnerMirror'),
            ('StockQuarterlyActivityCombined', 'CK_StockQuarterlyActivityCombined_CanonicalOwnerNotNull'),
            ('StockQuarterlyListingActivity', 'FK_StockQuarterlyListingActivity_EquityIssuer_EquityIssuerId'),
            ('StockQuarterlyListingActivity', 'CK_StockQuarterlyListingActivity_CanonicalOwnerMirror'),
            ('StockQuarterlyListingActivity', 'CK_StockQuarterlyListingActivity_CanonicalOwnerNotNull'),
            ('Form144Filing', 'FK_Form144Filing_EquityIssuer_EquityIssuerId'),
            ('Form144Filing', 'CK_Form144Filing_CanonicalOwnerMirror'),
            ('Form144Filing', 'CK_Form144Filing_CanonicalOwnerNotNull'),
            ('InsiderTransaction', 'FK_InsiderTransaction_EquityIssuer_EquityIssuerId'),
            ('InsiderTransaction', 'CK_InsiderTransaction_CanonicalOwnerMirror'),
            ('InsiderTransaction', 'CK_InsiderTransaction_CanonicalOwnerNotNull'),
            ('CompanyFilingSyncState', 'FK_CompanyFilingSyncState_EquityIssuer_EquityIssuerId'),
            ('CompanyFilingSyncState', 'CK_CompanyFilingSyncState_CanonicalOwnerMirror'),
            ('CompanyFilingSyncState', 'CK_CompanyFilingSyncState_CanonicalOwnerNotNull'),
            ('Document', 'FK_Document_EquityIssuer_EquityIssuerId'),
            ('Document', 'CK_Document_CanonicalOwnerMirror'),
            ('Document', 'CK_Document_CanonicalOwnerNotNull'),
            ('FormDFiling', 'FK_FormDFiling_EquityIssuer_EquityIssuerId'),
            ('FormDFiling', 'CK_FormDFiling_CanonicalOwnerMirror'),
            ('FormDFiling', 'CK_FormDFiling_CanonicalOwnerNotNull'),
            ('FundSeries', 'FK_FundSeries_EquityIssuer_EquityIssuerId'),
            ('FundSeries', 'CK_FundSeries_CanonicalOwnerMirror'),
            ('NCenFiling', 'FK_NCenFiling_EquityIssuer_EquityIssuerId'),
            ('NCenFiling', 'CK_NCenFiling_CanonicalOwnerMirror'),
            ('NCenFiling', 'CK_NCenFiling_CanonicalOwnerNotNull'),
            ('NportFiling', 'FK_NportFiling_EquityIssuer_EquityIssuerId'),
            ('NportFiling', 'CK_NportFiling_CanonicalOwnerMirror'),
            ('TranscriptCheckStatuses', 'FK_TranscriptCheckStatuses_EquityIssuer_EquityIssuerId'),
            ('TranscriptCheckStatuses', 'CK_TranscriptCheckStatuses_CanonicalOwnerMirror'),
            ('TranscriptCheckStatuses', 'CK_TranscriptCheckStatuses_CanonicalOwnerNotNull'),
            ('FinancialFact', 'FK_FinancialFact_EquityIssuer_EquityIssuerId'),
            ('FinancialFact', 'CK_FinancialFact_CanonicalOwnerMirror'),
            ('FinancialFact', 'CK_FinancialFact_CanonicalOwnerNotNull'),
            ('FinancialFactsSyncStatus', 'FK_FinancialFactsSyncStatus_EquityIssuer_EquityIssuerId'),
            ('FinancialFactsSyncStatus', 'CK_FinancialFactsSyncStatus_CanonicalOwnerMirror'),
            ('FinancialFactsSyncStatus', 'CK_FinancialFactsSyncStatus_CanonicalOwnerNotNull'),
            ('ListedSecurity', 'FK_ListedSecurity_EquityIssuer_EquityIssuerId'),
            ('ListedSecurity', 'CK_ListedSecurity_CanonicalOwnerMirror'),
            ('ListedSecurity', 'CK_ListedSecurity_CanonicalOwnerNotNull'),
            ('ReportedFinancialStatement', 'FK_ReportedFinancialStatement_EquityIssuer_EquityIssuerId'),
            ('ReportedFinancialStatement', 'CK_ReportedFinancialStatement_CanonicalOwnerMirror'),
            ('ReportedFinancialStatement', 'CK_ReportedFinancialStatement_CanonicalOwnerNotNull')
        ) expected(table_name, constraint_name)
    LOOP
        IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = to_regclass(format('%I', target.table_name)) AND conname = target.constraint_name AND convalidated) THEN
            RAISE EXCEPTION 'Canonical owner constraint %.% is unavailable', target.table_name, target.constraint_name;
        END IF;
    END LOOP;
END $audit$;
SELECT "TableName", "Completed", "UpdatedRows" FROM "EquityOwnerMigrationProgress" ORDER BY "TableName";
COMMIT;
