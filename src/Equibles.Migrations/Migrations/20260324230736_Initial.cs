using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_search", ",,")
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "Errors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: true),
                    Context = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Message = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    StackTrace = table.Column<string>(type: "text", nullable: true),
                    RequestSummary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Seen = table.Column<bool>(type: "boolean", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Errors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "File",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Extension = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ContentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_File", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Industry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Industry", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InstitutionalHolder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Cik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    City = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    StateOrCountry = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Form13FFileNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CrdNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstitutionalHolder", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileContent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Bytes = table.Column<byte[]>(type: "bytea", nullable: true),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileContent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileContent_File_FileId",
                        column: x => x.FileId,
                        principalTable: "File",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommonStock",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Ticker = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Cik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Website = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    MarketCapitalization = table.Column<double>(type: "double precision", nullable: false),
                    SharesOutStanding = table.Column<long>(type: "bigint", nullable: false),
                    SecondaryTickers = table.Column<List<string>>(type: "text[]", nullable: true),
                    Cusip = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    IndustryId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommonStock", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommonStock_Industry_IndustryId",
                        column: x => x.IndustryId,
                        principalTable: "Industry",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Document",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentType = table.Column<string>(type: "text", nullable: true),
                    ReportingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReportingForDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LineCount = table.Column<int>(type: "integer", nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Document", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Document_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Document_File_ContentId",
                        column: x => x.ContentId,
                        principalTable: "File",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InstitutionalHolding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstitutionalHolderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    FilingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    Shares = table.Column<long>(type: "bigint", nullable: false),
                    ShareType = table.Column<int>(type: "integer", nullable: false),
                    OptionType = table.Column<int>(type: "integer", nullable: true),
                    InvestmentDiscretion = table.Column<int>(type: "integer", nullable: false),
                    VotingAuthSole = table.Column<long>(type: "bigint", nullable: false),
                    VotingAuthShared = table.Column<long>(type: "bigint", nullable: false),
                    VotingAuthNone = table.Column<long>(type: "bigint", nullable: false),
                    TitleOfClass = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Cusip = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    AccessionNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IsAmendment = table.Column<bool>(type: "boolean", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstitutionalHolding", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstitutionalHolding_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InstitutionalHolding_InstitutionalHolder_InstitutionalHolde~",
                        column: x => x.InstitutionalHolderId,
                        principalTable: "InstitutionalHolder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Chunk",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Index = table.Column<int>(type: "integer", nullable: false),
                    StartPosition = table.Column<int>(type: "integer", nullable: false),
                    EndPosition = table.Column<int>(type: "integer", nullable: false),
                    StartLineNumber = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: true),
                    DocumentType = table.Column<string>(type: "text", nullable: true),
                    Ticker = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ReportingDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chunk", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Chunk_Document_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Document",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HoldingManagerEntry",
                columns: table => new
                {
                    InstitutionalHoldingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ManagerNumber = table.Column<int>(type: "integer", nullable: true),
                    ManagerName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Shares = table.Column<long>(type: "bigint", nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    InvestmentDiscretion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HoldingManagerEntry", x => new { x.InstitutionalHoldingId, x.Id });
                    table.ForeignKey(
                        name: "FK_HoldingManagerEntry_InstitutionalHolding_InstitutionalHoldi~",
                        column: x => x.InstitutionalHoldingId,
                        principalTable: "InstitutionalHolding",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Embedding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChunkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Model = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Vector = table.Column<Vector>(type: "vector", nullable: true),
                    VectorDimension = table.Column<int>(type: "integer", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Embedding", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Embedding_Chunk_ChunkId",
                        column: x => x.ChunkId,
                        principalTable: "Chunk",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Chunk_DocumentId_Index",
                table: "Chunk",
                columns: new[] { "DocumentId", "Index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Chunk_DocumentType",
                table: "Chunk",
                column: "DocumentType");

            migrationBuilder.CreateIndex(
                name: "IX_Chunk_Id_Content_DocumentType_DocumentId_Ticker_ReportingDa~",
                table: "Chunk",
                columns: new[] { "Id", "Content", "DocumentType", "DocumentId", "Ticker", "ReportingDate" })
                .Annotation("Npgsql:IndexMethod", "bm25")
                .Annotation("Npgsql:StorageParameter:key_field", "Id");

            migrationBuilder.CreateIndex(
                name: "IX_CommonStock_Cik",
                table: "CommonStock",
                column: "Cik",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommonStock_Cusip",
                table: "CommonStock",
                column: "Cusip");

            migrationBuilder.CreateIndex(
                name: "IX_CommonStock_IndustryId",
                table: "CommonStock",
                column: "IndustryId");

            migrationBuilder.CreateIndex(
                name: "IX_CommonStock_Ticker",
                table: "CommonStock",
                column: "Ticker",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Document_CommonStockId_DocumentType",
                table: "Document",
                columns: new[] { "CommonStockId", "DocumentType" });

            migrationBuilder.CreateIndex(
                name: "IX_Document_ContentId",
                table: "Document",
                column: "ContentId");

            migrationBuilder.CreateIndex(
                name: "IX_Document_DocumentType",
                table: "Document",
                column: "DocumentType");

            migrationBuilder.CreateIndex(
                name: "IX_Document_ReportingDate",
                table: "Document",
                column: "ReportingDate");

            migrationBuilder.CreateIndex(
                name: "IX_Document_ReportingForDate",
                table: "Document",
                column: "ReportingForDate");

            migrationBuilder.CreateIndex(
                name: "IX_Embedding_ChunkId_Model",
                table: "Embedding",
                columns: new[] { "ChunkId", "Model" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Errors_CreationTime",
                table: "Errors",
                column: "CreationTime");

            migrationBuilder.CreateIndex(
                name: "IX_Errors_Seen",
                table: "Errors",
                column: "Seen");

            migrationBuilder.CreateIndex(
                name: "IX_Errors_Source",
                table: "Errors",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_FileContent_FileId",
                table: "FileContent",
                column: "FileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalHolder_Cik",
                table: "InstitutionalHolder",
                column: "Cik",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalHolder_Name",
                table: "InstitutionalHolder",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalHolding_AccessionNumber",
                table: "InstitutionalHolding",
                column: "AccessionNumber");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalHolding_CommonStockId_InstitutionalHolderId_Re~",
                table: "InstitutionalHolding",
                columns: new[] { "CommonStockId", "InstitutionalHolderId", "ReportDate", "ShareType", "OptionType" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalHolding_CommonStockId_ReportDate",
                table: "InstitutionalHolding",
                columns: new[] { "CommonStockId", "ReportDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalHolding_FilingDate",
                table: "InstitutionalHolding",
                column: "FilingDate");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalHolding_InstitutionalHolderId_ReportDate",
                table: "InstitutionalHolding",
                columns: new[] { "InstitutionalHolderId", "ReportDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalHolding_ReportDate",
                table: "InstitutionalHolding",
                column: "ReportDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Embedding");

            migrationBuilder.DropTable(
                name: "Errors");

            migrationBuilder.DropTable(
                name: "FileContent");

            migrationBuilder.DropTable(
                name: "HoldingManagerEntry");

            migrationBuilder.DropTable(
                name: "Chunk");

            migrationBuilder.DropTable(
                name: "InstitutionalHolding");

            migrationBuilder.DropTable(
                name: "Document");

            migrationBuilder.DropTable(
                name: "InstitutionalHolder");

            migrationBuilder.DropTable(
                name: "CommonStock");

            migrationBuilder.DropTable(
                name: "File");

            migrationBuilder.DropTable(
                name: "Industry");
        }
    }
}
