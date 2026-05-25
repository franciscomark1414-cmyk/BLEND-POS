using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infoman.Migrations
{
    /// <inheritdoc />
    public partial class AddStockadjustmentLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockAdjustmentLogs",
                columns: table => new
                {
                    AdjustmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<int>(type: "int", nullable: false),
                    OldQuantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NewQuantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AdjustedBy = table.Column<int>(type: "int", nullable: false),
                    AdjustedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockAdjustmentLogs", x => x.AdjustmentId);
                    table.ForeignKey(
                        name: "FK_StockAdjustmentLogs_RawMaterialBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "RawMaterialBatches",
                        principalColumn: "BatchId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StockAdjustmentLogs_Users_AdjustedBy",
                        column: x => x.AdjustedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustmentLogs_AdjustedBy",
                table: "StockAdjustmentLogs",
                column: "AdjustedBy");

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustmentLogs_BatchId",
                table: "StockAdjustmentLogs",
                column: "BatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockAdjustmentLogs");
        }
    }
}
