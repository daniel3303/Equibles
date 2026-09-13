using Equibles.Migrations.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations;

public partial class ExpandCanonicalEquityOwnerColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        CanonicalEquityOwnerExpansion20260912.Apply(migrationBuilder,
        [
            new("CommonStockCusipAlias", "CommonStockId", ["Id"], true,
                [
                    ("IX_CommonStockCusipAlias_EquityIssuerId", "CREATE INDEX CONCURRENTLY \"IX_CommonStockCusipAlias_EquityIssuerId\" ON \"CommonStockCusipAlias\" (\"EquityIssuerId\");")
                ],
                [
                    ("FK_CommonStockCusipAlias_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_CommonStockCusipAlias_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")"),
                    ("CK_CommonStockCusipAlias_CanonicalOwnerNotNull", "CHECK (\"EquityIssuerId\" IS NOT NULL)")
                ]),
            new("CommonStockTickerAlias", "CommonStockId", ["Id"], true,
                [
                    ("IX_CommonStockTickerAlias_EquityIssuerId", "CREATE INDEX CONCURRENTLY \"IX_CommonStockTickerAlias_EquityIssuerId\" ON \"CommonStockTickerAlias\" (\"EquityIssuerId\");")
                ],
                [
                    ("FK_CommonStockTickerAlias_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_CommonStockTickerAlias_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")"),
                    ("CK_CommonStockTickerAlias_CanonicalOwnerNotNull", "CHECK (\"EquityIssuerId\" IS NOT NULL)")
                ]),
            new("CommonStockTickerEvidence", "CommonStockId", ["Id"], true,
                [
                    ("IX_CommonStockTickerEvidence_EquityIssuerId_Ticker_SourceDocum~", "CREATE UNIQUE INDEX CONCURRENTLY \"IX_CommonStockTickerEvidence_EquityIssuerId_Ticker_SourceDocum~\" ON \"CommonStockTickerEvidence\" (\"EquityIssuerId\", \"Ticker\", \"SourceDocumentId\");")
                ],
                [
                    ("FK_CommonStockTickerEvidence_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_CommonStockTickerEvidence_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")"),
                    ("CK_CommonStockTickerEvidence_CanonicalOwnerNotNull", "CHECK (\"EquityIssuerId\" IS NOT NULL)")
                ]),
            new("CommonStockListedCusip", "CommonStockId", ["Id"], true,
                [
                    ("IX_CommonStockListedCusip_EquityIssuerId", "CREATE INDEX CONCURRENTLY \"IX_CommonStockListedCusip_EquityIssuerId\" ON \"CommonStockListedCusip\" (\"EquityIssuerId\");")
                ],
                [
                    ("FK_CommonStockListedCusip_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_CommonStockListedCusip_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")"),
                    ("CK_CommonStockListedCusip_CanonicalOwnerNotNull", "CHECK (\"EquityIssuerId\" IS NOT NULL)")
                ]),
            new("CommonStockDelistedListing", "CommonStockId", ["Id"], true,
                [
                    ("IX_CommonStockDelistedListing_EquityIssuerId_ListedTicker", "CREATE UNIQUE INDEX CONCURRENTLY \"IX_CommonStockDelistedListing_EquityIssuerId_ListedTicker\" ON \"CommonStockDelistedListing\" (\"EquityIssuerId\", \"ListedTicker\");")
                ],
                [
                    ("FK_CommonStockDelistedListing_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_CommonStockDelistedListing_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")"),
                    ("CK_CommonStockDelistedListing_CanonicalOwnerNotNull", "CHECK (\"EquityIssuerId\" IS NOT NULL)")
                ]),
            new("CongressionalTrade", "CommonStockId", ["Id"], false,
                [
                    ("IX_CongressionalTrade_EquityIssuerId_TransactionDate", "CREATE INDEX CONCURRENTLY \"IX_CongressionalTrade_EquityIssuerId_TransactionDate\" ON \"CongressionalTrade\" (\"EquityIssuerId\", \"TransactionDate\");"),
                    ("IX_CongressionalTrade_LegacyFilingIdentity_CanonicalOwner", "CREATE INDEX CONCURRENTLY \"IX_CongressionalTrade_LegacyFilingIdentity_CanonicalOwner\" ON \"CongressionalTrade\" (\"EquityIssuerId\", \"CongressMemberId\", \"TransactionDate\", \"TransactionType\", \"AssetName\", \"OwnerType\", \"AmountFrom\", \"AmountTo\", \"AssetType\", \"Subholding\");")
                ],
                [
                    ("FK_CongressionalTrade_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_CongressionalTrade_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")")
                ]),
            new("CashDividend", "CommonStockId", ["Id"], true,
                [
                    ("IX_CashDividend_EquityIssuerId_ExDate", "CREATE UNIQUE INDEX CONCURRENTLY \"IX_CashDividend_EquityIssuerId_ExDate\" ON \"CashDividend\" (\"EquityIssuerId\", \"ExDate\") WHERE \"EquityListingId\" IS NULL;")
                ],
                [
                    ("FK_CashDividend_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_CashDividend_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")"),
                    ("CK_CashDividend_CanonicalOwnerNotNull", "CHECK (\"EquityIssuerId\" IS NOT NULL)")
                ]),
            new("StockSplit", "CommonStockId", ["Id"], true,
                [
                    ("IX_StockSplit_EquityIssuerId_EffectiveDate", "CREATE UNIQUE INDEX CONCURRENTLY \"IX_StockSplit_EquityIssuerId_EffectiveDate\" ON \"StockSplit\" (\"EquityIssuerId\", \"EffectiveDate\") WHERE \"PriceSeriesTicker\" IS NULL AND \"EquityListingId\" IS NULL;"),
                    ("IX_StockSplit_EquityIssuerId_PriceSeriesTicker_EffectiveDate", "CREATE UNIQUE INDEX CONCURRENTLY \"IX_StockSplit_EquityIssuerId_PriceSeriesTicker_EffectiveDate\" ON \"StockSplit\" (\"EquityIssuerId\", \"PriceSeriesTicker\", \"EffectiveDate\") WHERE \"PriceSeriesTicker\" IS NOT NULL AND \"EquityListingId\" IS NULL;")
                ],
                [
                    ("FK_StockSplit_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_StockSplit_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")"),
                    ("CK_StockSplit_CanonicalOwnerNotNull", "CHECK (\"EquityIssuerId\" IS NOT NULL)")
                ]),
            new("FdaCatalyst", "CommonStockId", ["Id"], false,
                [
                    ("IX_FdaCatalyst_EquityIssuerId", "CREATE INDEX CONCURRENTLY \"IX_FdaCatalyst_EquityIssuerId\" ON \"FdaCatalyst\" (\"EquityIssuerId\");")
                ],
                [
                    ("FK_FdaCatalyst_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_FdaCatalyst_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")")
                ]),
            new("GovernmentContract", "CommonStockId", ["Id"], true,
                [
                    ("IX_GovernmentContract_EquityIssuerId_ActionDate", "CREATE INDEX CONCURRENTLY \"IX_GovernmentContract_EquityIssuerId_ActionDate\" ON \"GovernmentContract\" (\"EquityIssuerId\", \"ActionDate\");")
                ],
                [
                    ("FK_GovernmentContract_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_GovernmentContract_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")"),
                    ("CK_GovernmentContract_CanonicalOwnerNotNull", "CHECK (\"EquityIssuerId\" IS NOT NULL)")
                ]),
            new("InstitutionalHolding", "CommonStockId", ["Id"], true,
                [
                    ("IX_InstitutionalHolding_EquityIssuerId_FilingDate", "CREATE INDEX CONCURRENTLY \"IX_InstitutionalHolding_EquityIssuerId_FilingDate\" ON \"InstitutionalHolding\" (\"EquityIssuerId\", \"FilingDate\") INCLUDE (\"AccessionNumber\", \"InstitutionalHolderId\");"),
                    ("IX_InstitutionalHolding_EquityIssuerId_InstitutionalHolderId_R~", "CREATE UNIQUE INDEX CONCURRENTLY \"IX_InstitutionalHolding_EquityIssuerId_InstitutionalHolderId_R~\" ON \"InstitutionalHolding\" (\"EquityIssuerId\", \"InstitutionalHolderId\", \"ReportDate\", \"ShareType\", \"OptionType\", \"FilingType\", \"ListedTicker\") NULLS NOT DISTINCT;"),
                    ("IX_InstitutionalHolding_InstitutionalHolderId_Report_0362a272bc", "CREATE INDEX CONCURRENTLY \"IX_InstitutionalHolding_InstitutionalHolderId_Report_0362a272bc\" ON \"InstitutionalHolding\" (\"InstitutionalHolderId\", \"ReportDate\") INCLUDE (\"EquityIssuerId\", \"Value\", \"Shares\", \"FilingDate\", \"FilingType\");"),
                    ("IX_InstitutionalHolding_ReportDate_EquityIssuerId_Institutiona~", "CREATE INDEX CONCURRENTLY \"IX_InstitutionalHolding_ReportDate_EquityIssuerId_Institutiona~\" ON \"InstitutionalHolding\" (\"ReportDate\", \"EquityIssuerId\", \"InstitutionalHolderId\") INCLUDE (\"Shares\", \"Value\");"),
                    ("IX_InstitutionalHolding_ReportDate_InstitutionalHolderId_Equit~", "CREATE INDEX CONCURRENTLY \"IX_InstitutionalHolding_ReportDate_InstitutionalHolderId_Equit~\" ON \"InstitutionalHolding\" (\"ReportDate\", \"InstitutionalHolderId\", \"EquityIssuerId\") INCLUDE (\"Shares\", \"Value\");"),
                    ("IX_InstitutionalHolding_StockQuarterCommonValue_CanonicalOwner", "CREATE INDEX CONCURRENTLY \"IX_InstitutionalHolding_StockQuarterCommonValue_CanonicalOwner\" ON \"InstitutionalHolding\" (\"EquityIssuerId\", \"ReportDate\", \"InstitutionalHolderId\") INCLUDE (\"Value\") WHERE \"FilingType\" = 0 AND \"OptionType\" IS NULL;"),
                    ("IX_InstitutionalHolding_StockQuarterExposure_CanonicalOwner", "CREATE INDEX CONCURRENTLY \"IX_InstitutionalHolding_StockQuarterExposure_CanonicalOwner\" ON \"InstitutionalHolding\" (\"EquityIssuerId\", \"ReportDate\") INCLUDE (\"InstitutionalHolderId\", \"Value\", \"Shares\", \"ListedTicker\", \"FilingType\", \"OptionType\");"),
                    ("IX_InstitutionalHolding_ValuePending_Pairs_CanonicalOwner", "CREATE INDEX CONCURRENTLY \"IX_InstitutionalHolding_ValuePending_Pairs_CanonicalOwner\" ON \"InstitutionalHolding\" (\"EquityIssuerId\", \"ListedTicker\", \"ReportDate\") WHERE \"ValuePending\";")
                ],
                [
                    ("FK_InstitutionalHolding_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_InstitutionalHolding_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")"),
                    ("CK_InstitutionalHolding_CanonicalOwnerNotNull", "CHECK (\"EquityIssuerId\" IS NOT NULL)")
                ]),
            new("StockQuarterlyActivity", "CommonStockId", ["CommonStockId", "ReportDate"], true,
                [
                    ("UX_StockQuarterlyActivity_CanonicalOwnerKey", "CREATE UNIQUE INDEX CONCURRENTLY \"UX_StockQuarterlyActivity_CanonicalOwnerKey\" ON \"StockQuarterlyActivity\" (\"EquityIssuerId\", \"ReportDate\");")
                ],
                [
                    ("FK_StockQuarterlyActivity_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_StockQuarterlyActivity_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")"),
                    ("CK_StockQuarterlyActivity_CanonicalOwnerNotNull", "CHECK (\"EquityIssuerId\" IS NOT NULL)")
                ]),
            new("StockQuarterlyActivityCombined", "CommonStockId", ["CommonStockId", "ReportDate"], true,
                [
                    ("UX_StockQuarterlyActivityCombined_CanonicalOwnerKey", "CREATE UNIQUE INDEX CONCURRENTLY \"UX_StockQuarterlyActivityCombined_CanonicalOwnerKey\" ON \"StockQuarterlyActivityCombined\" (\"EquityIssuerId\", \"ReportDate\");")
                ],
                [
                    ("FK_StockQuarterlyActivityCombined_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_StockQuarterlyActivityCombined_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")"),
                    ("CK_StockQuarterlyActivityCombined_CanonicalOwnerNotNull", "CHECK (\"EquityIssuerId\" IS NOT NULL)")
                ]),
            new("StockQuarterlyListingActivity", "CommonStockId", ["CommonStockId", "ReportDate", "IsCombined", "PriceSeriesTicker"], true,
                [
                    ("UX_StockQuarterlyListingActivity_CanonicalOwnerKey", "CREATE UNIQUE INDEX CONCURRENTLY \"UX_StockQuarterlyListingActivity_CanonicalOwnerKey\" ON \"StockQuarterlyListingActivity\" (\"EquityIssuerId\", \"ReportDate\", \"IsCombined\", \"PriceSeriesTicker\");")
                ],
                [
                    ("FK_StockQuarterlyListingActivity_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_StockQuarterlyListingActivity_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")"),
                    ("CK_StockQuarterlyListingActivity_CanonicalOwnerNotNull", "CHECK (\"EquityIssuerId\" IS NOT NULL)")
                ]),
            new("Form144Filing", "CommonStockId", ["Id"], true,
                [
                    ("IX_Form144Filing_EquityIssuerId_FilingDate", "CREATE INDEX CONCURRENTLY \"IX_Form144Filing_EquityIssuerId_FilingDate\" ON \"Form144Filing\" (\"EquityIssuerId\", \"FilingDate\");")
                ],
                [
                    ("FK_Form144Filing_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_Form144Filing_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")"),
                    ("CK_Form144Filing_CanonicalOwnerNotNull", "CHECK (\"EquityIssuerId\" IS NOT NULL)")
                ]),
            new("InsiderTransaction", "CommonStockId", ["Id"], true,
                [
                    ("IX_InsiderTransaction_EquityIssuerId_TransactionDate", "CREATE INDEX CONCURRENTLY \"IX_InsiderTransaction_EquityIssuerId_TransactionDate\" ON \"InsiderTransaction\" (\"EquityIssuerId\", \"TransactionDate\");"),
                    ("IX_InsiderTransaction_TransactionDate_Covering_CanonicalOwner", "CREATE INDEX CONCURRENTLY \"IX_InsiderTransaction_TransactionDate_Covering_CanonicalOwner\" ON \"InsiderTransaction\" (\"TransactionDate\") INCLUDE (\"Shares\", \"PricePerShare\", \"IsPriceValid\", \"SecurityKind\", \"SecurityTitle\", \"EquityIssuerId\", \"InsiderOwnerId\", \"TransactionCode\", \"IsRule10b5One\");")
                ],
                [
                    ("FK_InsiderTransaction_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_InsiderTransaction_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")"),
                    ("CK_InsiderTransaction_CanonicalOwnerNotNull", "CHECK (\"EquityIssuerId\" IS NOT NULL)")
                ]),
            new("CompanyFilingSyncState", "CommonStockId", ["CommonStockId"], true,
                [
                    ("UX_CompanyFilingSyncState_CanonicalOwnerKey", "CREATE UNIQUE INDEX CONCURRENTLY \"UX_CompanyFilingSyncState_CanonicalOwnerKey\" ON \"CompanyFilingSyncState\" (\"EquityIssuerId\");")
                ],
                [
                    ("FK_CompanyFilingSyncState_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_CompanyFilingSyncState_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")"),
                    ("CK_CompanyFilingSyncState_CanonicalOwnerNotNull", "CHECK (\"EquityIssuerId\" IS NOT NULL)")
                ]),
            new("Document", "CommonStockId", ["Id"], true,
                [
                    ("IX_Document_EquityIssuerId_DocumentType", "CREATE INDEX CONCURRENTLY \"IX_Document_EquityIssuerId_DocumentType\" ON \"Document\" (\"EquityIssuerId\", \"DocumentType\");")
                ],
                [
                    ("FK_Document_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_Document_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")"),
                    ("CK_Document_CanonicalOwnerNotNull", "CHECK (\"EquityIssuerId\" IS NOT NULL)")
                ]),
            new("FormDFiling", "CommonStockId", ["Id"], true,
                [
                    ("IX_FormDFiling_EquityIssuerId_FilingDate", "CREATE INDEX CONCURRENTLY \"IX_FormDFiling_EquityIssuerId_FilingDate\" ON \"FormDFiling\" (\"EquityIssuerId\", \"FilingDate\");")
                ],
                [
                    ("FK_FormDFiling_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_FormDFiling_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")"),
                    ("CK_FormDFiling_CanonicalOwnerNotNull", "CHECK (\"EquityIssuerId\" IS NOT NULL)")
                ]),
            new("FundSeries", "CommonStockId", ["Id"], false,
                [
                    ("IX_FundSeries_EquityIssuerId", "CREATE INDEX CONCURRENTLY \"IX_FundSeries_EquityIssuerId\" ON \"FundSeries\" (\"EquityIssuerId\");")
                ],
                [
                    ("FK_FundSeries_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_FundSeries_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")")
                ]),
            new("NCenFiling", "CommonStockId", ["Id"], true,
                [
                    ("IX_NCenFiling_EquityIssuerId_FilingDate", "CREATE INDEX CONCURRENTLY \"IX_NCenFiling_EquityIssuerId_FilingDate\" ON \"NCenFiling\" (\"EquityIssuerId\", \"FilingDate\");")
                ],
                [
                    ("FK_NCenFiling_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_NCenFiling_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")"),
                    ("CK_NCenFiling_CanonicalOwnerNotNull", "CHECK (\"EquityIssuerId\" IS NOT NULL)")
                ]),
            new("NportFiling", "CommonStockId", ["Id"], false,
                [
                    ("IX_NportFiling_EquityIssuerId_FilingDate", "CREATE INDEX CONCURRENTLY \"IX_NportFiling_EquityIssuerId_FilingDate\" ON \"NportFiling\" (\"EquityIssuerId\", \"FilingDate\");")
                ],
                [
                    ("FK_NportFiling_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_NportFiling_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")")
                ]),
            new("TranscriptCheckStatuses", "CommonStockId", ["Id"], true,
                [
                    ("IX_TranscriptCheckStatuses_EquityIssuerId", "CREATE UNIQUE INDEX CONCURRENTLY \"IX_TranscriptCheckStatuses_EquityIssuerId\" ON \"TranscriptCheckStatuses\" (\"EquityIssuerId\");")
                ],
                [
                    ("FK_TranscriptCheckStatuses_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_TranscriptCheckStatuses_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")"),
                    ("CK_TranscriptCheckStatuses_CanonicalOwnerNotNull", "CHECK (\"EquityIssuerId\" IS NOT NULL)")
                ]),
            new("FinancialFact", "CommonStockId", ["Id"], true,
                [
                    ("IX_FinancialFact_EquityIssuerId_FinancialConceptId_PeriodEnd", "CREATE INDEX CONCURRENTLY \"IX_FinancialFact_EquityIssuerId_FinancialConceptId_PeriodEnd\" ON \"FinancialFact\" (\"EquityIssuerId\", \"FinancialConceptId\", \"PeriodEnd\");"),
                    ("IX_FinancialFact_EquityIssuerId_FinancialConceptId_Unit_Period~", "CREATE UNIQUE INDEX CONCURRENTLY \"IX_FinancialFact_EquityIssuerId_FinancialConceptId_Unit_Period~\" ON \"FinancialFact\" (\"EquityIssuerId\", \"FinancialConceptId\", \"Unit\", \"PeriodStart\", \"PeriodEnd\", \"AccessionNumber\", \"DimensionsKey\");"),
                    ("IX_FinancialFact_EquityIssuerId_FiscalYear_FiscalPeriod", "CREATE INDEX CONCURRENTLY \"IX_FinancialFact_EquityIssuerId_FiscalYear_FiscalPeriod\" ON \"FinancialFact\" (\"EquityIssuerId\", \"FiscalYear\", \"FiscalPeriod\");")
                ],
                [
                    ("FK_FinancialFact_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_FinancialFact_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")"),
                    ("CK_FinancialFact_CanonicalOwnerNotNull", "CHECK (\"EquityIssuerId\" IS NOT NULL)")
                ]),
            new("FinancialFactsSyncStatus", "CommonStockId", ["Id"], true,
                [
                    ("IX_FinancialFactsSyncStatus_EquityIssuerId", "CREATE UNIQUE INDEX CONCURRENTLY \"IX_FinancialFactsSyncStatus_EquityIssuerId\" ON \"FinancialFactsSyncStatus\" (\"EquityIssuerId\");")
                ],
                [
                    ("FK_FinancialFactsSyncStatus_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_FinancialFactsSyncStatus_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")"),
                    ("CK_FinancialFactsSyncStatus_CanonicalOwnerNotNull", "CHECK (\"EquityIssuerId\" IS NOT NULL)")
                ]),
            new("ListedSecurity", "CommonStockId", ["Id"], true,
                [
                    ("IX_ListedSecurity_EquityIssuerId_TradingSymbol", "CREATE UNIQUE INDEX CONCURRENTLY \"IX_ListedSecurity_EquityIssuerId_TradingSymbol\" ON \"ListedSecurity\" (\"EquityIssuerId\", \"TradingSymbol\");")
                ],
                [
                    ("FK_ListedSecurity_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_ListedSecurity_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")"),
                    ("CK_ListedSecurity_CanonicalOwnerNotNull", "CHECK (\"EquityIssuerId\" IS NOT NULL)")
                ]),
            new("ReportedFinancialStatement", "CommonStockId", ["Id"], true,
                [
                    ("IX_ReportedFinancialStatement_EquityIssuerId_Kind_FiscalYear_F~", "CREATE INDEX CONCURRENTLY \"IX_ReportedFinancialStatement_EquityIssuerId_Kind_FiscalYear_F~\" ON \"ReportedFinancialStatement\" (\"EquityIssuerId\", \"Kind\", \"FiscalYear\", \"FiscalPeriod\");")
                ],
                [
                    ("FK_ReportedFinancialStatement_EquityIssuer_EquityIssuerId", "FOREIGN KEY (\"EquityIssuerId\") REFERENCES \"EquityIssuer\" (\"Id\") ON DELETE RESTRICT"),
                    ("CK_ReportedFinancialStatement_CanonicalOwnerMirror", "CHECK (\"EquityIssuerId\" IS NOT DISTINCT FROM \"CommonStockId\")"),
                    ("CK_ReportedFinancialStatement_CanonicalOwnerNotNull", "CHECK (\"EquityIssuerId\" IS NOT NULL)")
                ])
        ]);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new System.NotSupportedException("Canonical ownership is a lossless forward migration; rollback must retain both column generations.");
}
