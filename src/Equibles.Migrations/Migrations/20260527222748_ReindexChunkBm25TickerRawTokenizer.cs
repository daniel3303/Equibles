using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ReindexChunkBm25TickerRawTokenizer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Chunk_Id_Content_DocumentType_DocumentId_Ticker_ReportingDa~",
                table: "Chunk"
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_Chunk_Id_Content_DocumentType_DocumentId_Ticker_ReportingDa~",
                    table: "Chunk",
                    columns: new[]
                    {
                        "Id",
                        "Content",
                        "DocumentType",
                        "DocumentId",
                        "Ticker",
                        "ReportingDate",
                    }
                )
                .Annotation("Npgsql:IndexMethod", "bm25")
                .Annotation("Npgsql:StorageParameter:key_field", "Id")
                .Annotation(
                    "Npgsql:StorageParameter:text_fields",
                    "{\"Ticker\":{\"tokenizer\":{\"type\":\"raw\"},\"fast\":true}}"
                );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Chunk_Id_Content_DocumentType_DocumentId_Ticker_ReportingDa~",
                table: "Chunk"
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_Chunk_Id_Content_DocumentType_DocumentId_Ticker_ReportingDa~",
                    table: "Chunk",
                    columns: new[]
                    {
                        "Id",
                        "Content",
                        "DocumentType",
                        "DocumentId",
                        "Ticker",
                        "ReportingDate",
                    }
                )
                .Annotation("Npgsql:IndexMethod", "bm25")
                .Annotation("Npgsql:StorageParameter:key_field", "Id");
        }
    }
}
