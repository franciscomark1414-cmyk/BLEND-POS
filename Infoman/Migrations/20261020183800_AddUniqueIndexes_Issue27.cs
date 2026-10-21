using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infoman.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueIndexes_Issue27 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmployeeAttendances_UserId",
                table: "EmployeeAttendances");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturns_ReturnNumber",
                table: "SupplierReturns",
                column: "ReturnNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Receivings_DRNumber",
                table: "Receivings",
                column: "DRNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterials_SKU",
                table: "RawMaterials",
                column: "SKU",
                unique: true,
                filter: "[SKU] IS NOT NULL AND [SKU] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSizes_SKU",
                table: "ProductSizes",
                column: "SKU",
                unique: true,
                filter: "[SKU] IS NOT NULL AND [SKU] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_Products_SKU",
                table: "Products",
                column: "SKU",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ReceiptNumber",
                table: "Orders",
                column: "ReceiptNumber",
                unique: true,
                filter: "[ReceiptNumber] IS NOT NULL AND [ReceiptNumber] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAttendances_UserId_Date",
                table: "EmployeeAttendances",
                columns: new[] { "UserId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SupplierReturns_ReturnNumber",
                table: "SupplierReturns");

            migrationBuilder.DropIndex(
                name: "IX_Receivings_DRNumber",
                table: "Receivings");

            migrationBuilder.DropIndex(
                name: "IX_RawMaterials_SKU",
                table: "RawMaterials");

            migrationBuilder.DropIndex(
                name: "IX_ProductSizes_SKU",
                table: "ProductSizes");

            migrationBuilder.DropIndex(
                name: "IX_Products_SKU",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Orders_ReceiptNumber",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeAttendances_UserId_Date",
                table: "EmployeeAttendances");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAttendances_UserId",
                table: "EmployeeAttendances",
                column: "UserId");
        }
    }
}
