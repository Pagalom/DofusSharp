using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BestCrush.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddCrushHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrushHistorySessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ServerName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TotalValue = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrushHistorySessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrushHistoryEquipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DofusDbId = table.Column<long>(type: "INTEGER", nullable: false),
                    DofusDbIconId = table.Column<long>(type: "INTEGER", nullable: true),
                    EquipmentName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CoefficientPercent = table.Column<double>(type: "REAL", nullable: false),
                    CoefficientSource = table.Column<int>(type: "INTEGER", nullable: false),
                    RowY = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrushHistoryEquipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrushHistoryEquipments_CrushHistorySessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "CrushHistorySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CrushHistoryRunes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DofusDbId = table.Column<long>(type: "INTEGER", nullable: false),
                    DofusDbIconId = table.Column<long>(type: "INTEGER", nullable: true),
                    RuneName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrushHistoryRunes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrushHistoryRunes_CrushHistorySessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "CrushHistorySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CrushHistoryRuneLots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RuneId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Count = table.Column<long>(type: "INTEGER", nullable: false),
                    LotQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    LotPrice = table.Column<long>(type: "INTEGER", nullable: false),
                    IsEstimated = table.Column<bool>(type: "INTEGER", nullable: false),
                    PriceSource = table.Column<int>(type: "INTEGER", nullable: true),
                    PriceObservedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrushHistoryRuneLots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrushHistoryRuneLots_CrushHistoryRunes_RuneId",
                        column: x => x.RuneId,
                        principalTable: "CrushHistoryRunes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrushHistoryEquipments_SessionId_DofusDbId",
                table: "CrushHistoryEquipments",
                columns: new[] { "SessionId", "DofusDbId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrushHistoryRuneLots_RuneId",
                table: "CrushHistoryRuneLots",
                column: "RuneId");

            migrationBuilder.CreateIndex(
                name: "IX_CrushHistoryRunes_SessionId_DofusDbId",
                table: "CrushHistoryRunes",
                columns: new[] { "SessionId", "DofusDbId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrushHistorySessions_ServerName_CompletedAtUtc",
                table: "CrushHistorySessions",
                columns: new[] { "ServerName", "CompletedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrushHistoryEquipments");

            migrationBuilder.DropTable(
                name: "CrushHistoryRuneLots");

            migrationBuilder.DropTable(
                name: "CrushHistoryRunes");

            migrationBuilder.DropTable(
                name: "CrushHistorySessions");
        }
    }
}
