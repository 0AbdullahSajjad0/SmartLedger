using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartLedger.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Budgets get a history: one row per change, so changing a budget no longer rewrites past months. Each existing
    /// budget is copied into a row starting from the month its category was created (the months it really applied to),
    /// before the old column is dropped. Archived categories already left the totals, so they get no row.
    /// </summary>
    public partial class AddBudgetHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CategoryBudgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryBudgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CategoryBudgets_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CategoryBudgets_CategoryId_EffectiveFrom",
                table: "CategoryBudgets",
                columns: new[] { "CategoryId", "EffectiveFrom" },
                unique: true);

            // Copy before dropping: every active category's budget, from the first of the month it was created.
            migrationBuilder.Sql("""
                INSERT INTO [CategoryBudgets] ([Id], [CategoryId], [EffectiveFrom], [Amount], [CreatedAt], [UpdatedAt])
                SELECT NEWID(), [Id], DATEFROMPARTS(YEAR([CreatedAt]), MONTH([CreatedAt]), 1), [MonthlyBudget],
                       SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()
                FROM [Categories]
                WHERE [MonthlyBudget] IS NOT NULL AND [IsArchived] = 0;
                """);

            migrationBuilder.DropColumn(
                name: "MonthlyBudget",
                table: "Categories");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyBudget",
                table: "Categories",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            // Back to one budget per category: the latest one.
            migrationBuilder.Sql("""
                UPDATE [c] SET [MonthlyBudget] = [latest].[Amount]
                FROM [Categories] AS [c]
                CROSS APPLY (
                    SELECT TOP (1) [b].[Amount] FROM [CategoryBudgets] AS [b]
                    WHERE [b].[CategoryId] = [c].[Id]
                    ORDER BY [b].[EffectiveFrom] DESC
                ) AS [latest];
                """);

            migrationBuilder.DropTable(
                name: "CategoryBudgets");
        }
    }
}
