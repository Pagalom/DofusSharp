using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BestCrush.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketPriceHistoryIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_MarketPriceObservations_ObjectType_DofusDbId_ServerName_Quantity_Source_ObservedAtUtc",
                table: "MarketPriceObservations",
                columns: new[] { "ObjectType", "DofusDbId", "ServerName", "Quantity", "Source", "ObservedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MarketPriceObservations_ObjectType_DofusDbId_ServerName_Quantity_Source_ObservedAtUtc",
                table: "MarketPriceObservations");
        }
    }
}
