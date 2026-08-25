using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BestCrush.Domain.Migrations
{
    /// <inheritdoc />
    public partial class SyncMarketPriceModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MarketPriceObservations_ObjectType_DofusDbId_ServerName_ObservedAtUtc",
                table: "MarketPriceObservations");

            migrationBuilder.CreateIndex(
                name: "IX_MarketPriceObservations_ObjectType_DofusDbId_ServerName_Quantity_ObservedAtUtc",
                table: "MarketPriceObservations",
                columns: new[] { "ObjectType", "DofusDbId", "ServerName", "Quantity", "ObservedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MarketPriceObservations_ObjectType_DofusDbId_ServerName_Quantity_ObservedAtUtc",
                table: "MarketPriceObservations");

            migrationBuilder.CreateIndex(
                name: "IX_MarketPriceObservations_ObjectType_DofusDbId_ServerName_ObservedAtUtc",
                table: "MarketPriceObservations",
                columns: new[] { "ObjectType", "DofusDbId", "ServerName", "ObservedAtUtc" });
        }
    }
}
