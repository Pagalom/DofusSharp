using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BestCrush.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddObservationResetState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCleared",
                table: "MarketPriceObservations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsCleared",
                table: "CoefficientObservations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsCleared",
                table: "MarketPriceObservations");

            migrationBuilder.DropColumn(
                name: "IsCleared",
                table: "CoefficientObservations");
        }
    }
}
