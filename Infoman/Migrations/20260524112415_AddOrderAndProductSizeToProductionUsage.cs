using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infoman.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderAndProductSizeToProductionUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OrderId",
                table: "ProductionUsages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProductSizeId",
                table: "ProductionUsages",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionUsages_OrderId",
                table: "ProductionUsages",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionUsages_ProductSizeId",
                table: "ProductionUsages",
                column: "ProductSizeId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionUsages_Orders_OrderId",
                table: "ProductionUsages",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "OrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionUsages_ProductSizes_ProductSizeId",
                table: "ProductionUsages",
                column: "ProductSizeId",
                principalTable: "ProductSizes",
                principalColumn: "ProductSizeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductionUsages_Orders_OrderId",
                table: "ProductionUsages");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductionUsages_ProductSizes_ProductSizeId",
                table: "ProductionUsages");

            migrationBuilder.DropIndex(
                name: "IX_ProductionUsages_OrderId",
                table: "ProductionUsages");

            migrationBuilder.DropIndex(
                name: "IX_ProductionUsages_ProductSizeId",
                table: "ProductionUsages");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "ProductionUsages");

            migrationBuilder.DropColumn(
                name: "ProductSizeId",
                table: "ProductionUsages");
        }
    }
}
