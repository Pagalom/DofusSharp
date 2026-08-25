using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BestCrush.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalMarketPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarketPriceObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ObjectType = table.Column<int>(type: "INTEGER", nullable: false),
                    DofusDbId = table.Column<long>(type: "INTEGER", nullable: false),
                    ServerName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Price = table.Column<long>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    ObservedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketPriceObservations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketPriceObservations_ObjectType_DofusDbId_ServerName_ObservedAtUtc",
                table: "MarketPriceObservations",
                columns: new[] { "ObjectType", "DofusDbId", "ServerName", "ObservedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketPriceObservations");
        }
    }
}
