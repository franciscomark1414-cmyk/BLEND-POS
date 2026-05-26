using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infoman.Migrations
{
    /// <inheritdoc />
    public partial class AddWasteLogsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WasteLogs",
                columns: table => new
                {
                    WasteLogId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RawMaterialId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TotalCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BatchId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WasteLogs", x => x.WasteLogId);
                    table.ForeignKey(
                        name: "FK_WasteLogs_RawMaterials_RawMaterialId",
                        column: x => x.RawMaterialId,
                        principalTable: "RawMaterials",
                        principalColumn: "RawMaterialId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WasteLogs_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId");
                    table.ForeignKey(
                        name: "FK_WasteLogs_RawMaterialBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "RawMaterialBatches",
                        principalColumn: "BatchId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WasteLogs_BatchId",
                table: "WasteLogs",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_WasteLogs_ProductId",
                table: "WasteLogs",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_WasteLogs_RawMaterialId",
                table: "WasteLogs",
                column: "RawMaterialId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WasteLogs");
        }
    }
}