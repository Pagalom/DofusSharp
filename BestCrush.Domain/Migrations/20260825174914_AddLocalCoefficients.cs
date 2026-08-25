using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BestCrush.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalCoefficients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CoefficientObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DofusDbId = table.Column<long>(type: "INTEGER", nullable: false),
                    ServerName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CoefficientPercent = table.Column<double>(type: "REAL", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    ObservedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoefficientObservations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoefficientObservations_DofusDbId_ServerName_ObservedAtUtc",
                table: "CoefficientObservations",
                columns: new[] { "DofusDbId", "ServerName", "ObservedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoefficientObservations");
        }
    }
}
