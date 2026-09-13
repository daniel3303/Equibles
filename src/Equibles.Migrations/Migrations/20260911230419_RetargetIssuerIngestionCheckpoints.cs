using Equibles.Migrations.Infrastructure;
﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RetargetIssuerIngestionCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "CompanyFilingSyncState", "CommonStockId", "FK_CompanyFilingSyncState_EquityIssuer_CommonStockId", "FK_CompanyFilingSyncState_CommonStock_CommonStockId");

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "FinancialFactsSyncStatus", "CommonStockId", "FK_FinancialFactsSyncStatus_EquityIssuer_CommonStockId", "FK_FinancialFactsSyncStatus_CommonStock_CommonStockId");

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "TranscriptCheckStatuses", "CommonStockId", "FK_TranscriptCheckStatuses_EquityIssuer_CommonStockId", "FK_TranscriptCheckStatuses_CommonStock_CommonStockId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Retain native issuer references when reverting application binaries.");
        }
    }
}
