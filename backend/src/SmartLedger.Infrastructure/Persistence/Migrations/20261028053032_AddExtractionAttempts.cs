using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartLedger.Infrastructure.Persistence.Migrations
{
    public partial class AddExtractionAttempts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExtractionAttempts",
                table: "Receipts",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtractionAttempts",
                table: "Receipts");
        }
    }
}
