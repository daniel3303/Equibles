using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ReshapeFdaCatalystForFdaGovSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PublicationDate",
                table: "FdaCatalyst",
                newName: "EndDate");

            migrationBuilder.RenameColumn(
                name: "Committee",
                table: "FdaCatalyst",
                newName: "Center");

            migrationBuilder.AlterColumn<string>(
                name: "SourceReference",
                table: "FdaCatalyst",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "EndDate",
                table: "FdaCatalyst",
                newName: "PublicationDate");

            migrationBuilder.RenameColumn(
                name: "Center",
                table: "FdaCatalyst",
                newName: "Committee");

            migrationBuilder.AlterColumn<string>(
                name: "SourceReference",
                table: "FdaCatalyst",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);
        }
    }
}
