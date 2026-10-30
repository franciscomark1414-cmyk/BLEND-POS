using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infoman.Migrations
{
    /// <inheritdoc />
    public partial class FixStoreNameAndSupplierId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Receivings_Suppliers_SupplierId",
                table: "Receivings");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplierReturns_Suppliers_SupplierId",
                table: "SupplierReturns");

            migrationBuilder.AlterColumn<int>(
                name: "SupplierId",
                table: "SupplierReturns",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "SupplierId",
                table: "Receivings",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddForeignKey(
                name: "FK_Receivings_Suppliers_SupplierId",
                table: "Receivings",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "SupplierId");

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierReturns_Suppliers_SupplierId",
                table: "SupplierReturns",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Receivings_Suppliers_SupplierId",
                table: "Receivings");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplierReturns_Suppliers_SupplierId",
                table: "SupplierReturns");

            migrationBuilder.AlterColumn<int>(
                name: "SupplierId",
                table: "SupplierReturns",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "SupplierId",
                table: "Receivings",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Receivings_Suppliers_SupplierId",
                table: "Receivings",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "SupplierId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierReturns_Suppliers_SupplierId",
                table: "SupplierReturns",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "SupplierId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
