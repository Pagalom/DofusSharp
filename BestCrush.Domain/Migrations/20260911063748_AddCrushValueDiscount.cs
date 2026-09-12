using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BestCrush.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddCrushValueDiscount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DiscountPercent",
                table: "CrushHistorySessions",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "DiscountedTotalValue",
                table: "CrushHistorySessions",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscountPercent",
                table: "CrushHistorySessions");

            migrationBuilder.DropColumn(
                name: "DiscountedTotalValue",
                table: "CrushHistorySessions");
        }
    }
}
