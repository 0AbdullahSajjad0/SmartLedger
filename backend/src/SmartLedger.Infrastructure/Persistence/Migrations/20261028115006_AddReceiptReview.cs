using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartLedger.Infrastructure.Persistence.Migrations
{
    public partial class AddReceiptReview : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [ExchangeRates];");

            migrationBuilder.DropColumn(
                name: "RateToUsd",
                table: "ExchangeRates");

            migrationBuilder.AlterColumn<decimal>(
                name: "ExchangeRate",
                table: "Receipts",
                type: "decimal(20,10)",
                precision: 20,
                scale: 10,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,8)",
                oldPrecision: 18,
                oldScale: 8,
                oldNullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AmountCharged",
                table: "Receipts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExchangeRateDate",
                table: "Receipts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitsPerUsd",
                table: "ExchangeRates",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AmountCharged",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "ExchangeRateDate",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "UnitsPerUsd",
                table: "ExchangeRates");

            migrationBuilder.AlterColumn<decimal>(
                name: "ExchangeRate",
                table: "Receipts",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(20,10)",
                oldPrecision: 20,
                oldScale: 10,
                oldNullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RateToUsd",
                table: "ExchangeRates",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);
        }
    }
}
