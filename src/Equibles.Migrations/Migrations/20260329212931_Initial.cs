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
                name: "CboePutCallRatio",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RatioType = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    CallVolume = table.Column<long>(type: "bigint", nullable: true),
                    PutVolume = table.Column<long>(type: "bigint", nullable: true),
                    TotalVolume = table.Column<long>(type: "bigint", nullable: true),
                    PutCallRatio = table.Column<decimal>(type: "numeric", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CboePutCallRatio", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CboeVixDaily",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Open = table.Column<decimal>(type: "numeric", nullable: false),
                    High = table.Column<decimal>(type: "numeric", nullable: false),
                    Low = table.Column<decimal>(type: "numeric", nullable: false),
                    Close = table.Column<decimal>(type: "numeric", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CboeVixDaily", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CftcContract",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MarketName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    LatestReportDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CftcContract", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CongressMember",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CongressMember", x => x.Id);
                });

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
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Discriminator = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_File", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FredSeries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Frequency = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Units = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SeasonalAdjustment = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ObservationStart = table.Column<DateOnly>(type: "date", nullable: true),
                    ObservationEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FredSeries", x => x.Id);
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
                name: "InsiderOwner",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerCik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    City = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    StateOrCountry = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsDirector = table.Column<bool>(type: "boolean", nullable: false),
                    IsOfficer = table.Column<bool>(type: "boolean", nullable: false),
                    OfficerTitle = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsTenPercentOwner = table.Column<bool>(type: "boolean", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsiderOwner", x => x.Id);
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
                name: "CftcPositionReport",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CftcContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OpenInterest = table.Column<long>(type: "bigint", nullable: false),
                    NonCommLong = table.Column<long>(type: "bigint", nullable: false),
                    NonCommShort = table.Column<long>(type: "bigint", nullable: false),
                    NonCommSpreads = table.Column<long>(type: "bigint", nullable: false),
                    CommLong = table.Column<long>(type: "bigint", nullable: false),
                    CommShort = table.Column<long>(type: "bigint", nullable: false),
                    TotalRptLong = table.Column<long>(type: "bigint", nullable: false),
                    TotalRptShort = table.Column<long>(type: "bigint", nullable: false),
                    NonRptLong = table.Column<long>(type: "bigint", nullable: false),
                    NonRptShort = table.Column<long>(type: "bigint", nullable: false),
                    ChangeOpenInterest = table.Column<long>(type: "bigint", nullable: true),
                    ChangeNonCommLong = table.Column<long>(type: "bigint", nullable: true),
                    ChangeNonCommShort = table.Column<long>(type: "bigint", nullable: true),
                    ChangeCommLong = table.Column<long>(type: "bigint", nullable: true),
                    ChangeCommShort = table.Column<long>(type: "bigint", nullable: true),
                    PctNonCommLong = table.Column<decimal>(type: "numeric", nullable: true),
                    PctNonCommShort = table.Column<decimal>(type: "numeric", nullable: true),
                    PctCommLong = table.Column<decimal>(type: "numeric", nullable: true),
                    PctCommShort = table.Column<decimal>(type: "numeric", nullable: true),
                    TradersTotal = table.Column<int>(type: "integer", nullable: true),
                    TradersNonCommLong = table.Column<int>(type: "integer", nullable: true),
                    TradersNonCommShort = table.Column<int>(type: "integer", nullable: true),
                    TradersCommLong = table.Column<int>(type: "integer", nullable: true),
                    TradersCommShort = table.Column<int>(type: "integer", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CftcPositionReport", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CftcPositionReport_CftcContract_CftcContractId",
                        column: x => x.CftcContractId,
                        principalTable: "CftcContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "FredObservation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FredSeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Value = table.Column<decimal>(type: "numeric", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FredObservation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FredObservation_FredSeries_FredSeriesId",
                        column: x => x.FredSeriesId,
                        principalTable: "FredSeries",
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
                    IndustryId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommonStock", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommonStock_Industry_IndustryId",
                        column: x => x.IndustryId,
                        principalTable: "Industry",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CongressionalTrade",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CongressMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FilingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TransactionType = table.Column<int>(type: "integer", nullable: false),
                    OwnerType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AssetName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AmountFrom = table.Column<long>(type: "bigint", nullable: false),
                    AmountTo = table.Column<long>(type: "bigint", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CongressionalTrade", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CongressionalTrade_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CongressionalTrade_CongressMember_CongressMemberId",
                        column: x => x.CongressMemberId,
                        principalTable: "CongressMember",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DailyShortVolume",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ShortVolume = table.Column<long>(type: "bigint", nullable: false),
                    ShortExemptVolume = table.Column<long>(type: "bigint", nullable: false),
                    TotalVolume = table.Column<long>(type: "bigint", nullable: false),
                    Market = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyShortVolume", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyShortVolume_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DailyStockPrice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Open = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    High = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Low = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Close = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    AdjustedClose = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Volume = table.Column<long>(type: "bigint", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyStockPrice", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyStockPrice_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
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
                name: "FailToDeliver",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    SettlementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Quantity = table.Column<long>(type: "bigint", nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailToDeliver", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FailToDeliver_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InsiderTransaction",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InsiderOwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    FilingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TransactionCode = table.Column<int>(type: "integer", nullable: false),
                    Shares = table.Column<long>(type: "bigint", nullable: false),
                    PricePerShare = table.Column<decimal>(type: "numeric", nullable: false),
                    AcquiredDisposed = table.Column<int>(type: "integer", nullable: false),
                    SharesOwnedAfter = table.Column<long>(type: "bigint", nullable: false),
                    OwnershipNature = table.Column<int>(type: "integer", nullable: false),
                    SecurityTitle = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AccessionNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IsAmendment = table.Column<bool>(type: "boolean", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsiderTransaction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InsiderTransaction_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InsiderTransaction_InsiderOwner_InsiderOwnerId",
                        column: x => x.InsiderOwnerId,
                        principalTable: "InsiderOwner",
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
                    ValuePending = table.Column<bool>(type: "boolean", nullable: false),
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
                name: "ShortInterest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    SettlementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CurrentShortPosition = table.Column<long>(type: "bigint", nullable: false),
                    PreviousShortPosition = table.Column<long>(type: "bigint", nullable: false),
                    ChangeInShortPosition = table.Column<long>(type: "bigint", nullable: false),
                    AverageDailyVolume = table.Column<long>(type: "bigint", nullable: true),
                    DaysToCover = table.Column<decimal>(type: "numeric", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShortInterest", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShortInterest_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
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
                name: "IX_CboePutCallRatio_Date",
                table: "CboePutCallRatio",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_CboePutCallRatio_RatioType_Date",
                table: "CboePutCallRatio",
                columns: new[] { "RatioType", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CboeVixDaily_Date",
                table: "CboeVixDaily",
                column: "Date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CftcContract_Category",
                table: "CftcContract",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_CftcContract_MarketCode",
                table: "CftcContract",
                column: "MarketCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CftcPositionReport_CftcContractId_ReportDate",
                table: "CftcPositionReport",
                columns: new[] { "CftcContractId", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CftcPositionReport_ReportDate",
                table: "CftcPositionReport",
                column: "ReportDate");

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
                name: "IX_CongressionalTrade_CommonStockId_CongressMemberId_Transacti~",
                table: "CongressionalTrade",
                columns: new[] { "CommonStockId", "CongressMemberId", "TransactionDate", "TransactionType", "AssetName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CongressionalTrade_CommonStockId_TransactionDate",
                table: "CongressionalTrade",
                columns: new[] { "CommonStockId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CongressionalTrade_CongressMemberId_TransactionDate",
                table: "CongressionalTrade",
                columns: new[] { "CongressMemberId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CongressionalTrade_FilingDate",
                table: "CongressionalTrade",
                column: "FilingDate");

            migrationBuilder.CreateIndex(
                name: "IX_CongressionalTrade_TransactionDate",
                table: "CongressionalTrade",
                column: "TransactionDate");

            migrationBuilder.CreateIndex(
                name: "IX_CongressMember_Name",
                table: "CongressMember",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyShortVolume_CommonStockId_Date",
                table: "DailyShortVolume",
                columns: new[] { "CommonStockId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyShortVolume_Date",
                table: "DailyShortVolume",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_DailyStockPrice_CommonStockId_Date",
                table: "DailyStockPrice",
                columns: new[] { "CommonStockId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyStockPrice_Date",
                table: "DailyStockPrice",
                column: "Date");

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
                name: "IX_FailToDeliver_CommonStockId_SettlementDate",
                table: "FailToDeliver",
                columns: new[] { "CommonStockId", "SettlementDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FailToDeliver_SettlementDate",
                table: "FailToDeliver",
                column: "SettlementDate");

            migrationBuilder.CreateIndex(
                name: "IX_FileContent_FileId",
                table: "FileContent",
                column: "FileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FredObservation_Date",
                table: "FredObservation",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_FredObservation_FredSeriesId_Date",
                table: "FredObservation",
                columns: new[] { "FredSeriesId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FredSeries_Category",
                table: "FredSeries",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_FredSeries_SeriesId",
                table: "FredSeries",
                column: "SeriesId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InsiderOwner_Name",
                table: "InsiderOwner",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_InsiderOwner_OwnerCik",
                table: "InsiderOwner",
                column: "OwnerCik",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_AccessionNumber",
                table: "InsiderTransaction",
                column: "AccessionNumber");

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_CommonStockId_InsiderOwnerId_Transaction~",
                table: "InsiderTransaction",
                columns: new[] { "CommonStockId", "InsiderOwnerId", "TransactionDate", "TransactionCode", "SecurityTitle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_CommonStockId_TransactionDate",
                table: "InsiderTransaction",
                columns: new[] { "CommonStockId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_FilingDate",
                table: "InsiderTransaction",
                column: "FilingDate");

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_InsiderOwnerId_TransactionDate",
                table: "InsiderTransaction",
                columns: new[] { "InsiderOwnerId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_TransactionDate",
                table: "InsiderTransaction",
                column: "TransactionDate");

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

            migrationBuilder.CreateIndex(
                name: "IX_ShortInterest_CommonStockId_SettlementDate",
                table: "ShortInterest",
                columns: new[] { "CommonStockId", "SettlementDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShortInterest_SettlementDate",
                table: "ShortInterest",
                column: "SettlementDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CboePutCallRatio");

            migrationBuilder.DropTable(
                name: "CboeVixDaily");

            migrationBuilder.DropTable(
                name: "CftcPositionReport");

            migrationBuilder.DropTable(
                name: "CongressionalTrade");

            migrationBuilder.DropTable(
                name: "DailyShortVolume");

            migrationBuilder.DropTable(
                name: "DailyStockPrice");

            migrationBuilder.DropTable(
                name: "Embedding");

            migrationBuilder.DropTable(
                name: "Errors");

            migrationBuilder.DropTable(
                name: "FailToDeliver");

            migrationBuilder.DropTable(
                name: "FileContent");

            migrationBuilder.DropTable(
                name: "FredObservation");

            migrationBuilder.DropTable(
                name: "HoldingManagerEntry");

            migrationBuilder.DropTable(
                name: "InsiderTransaction");

            migrationBuilder.DropTable(
                name: "ShortInterest");

            migrationBuilder.DropTable(
                name: "CftcContract");

            migrationBuilder.DropTable(
                name: "CongressMember");

            migrationBuilder.DropTable(
                name: "Chunk");

            migrationBuilder.DropTable(
                name: "FredSeries");

            migrationBuilder.DropTable(
                name: "InstitutionalHolding");

            migrationBuilder.DropTable(
                name: "InsiderOwner");

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
