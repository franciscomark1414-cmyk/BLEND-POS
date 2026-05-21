using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infoman.Migrations
{
    /// <inheritdoc />
    public partial class addSuplierReturntbl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreditAmount",
                table: "SupplierReturns");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturns_ReceivingItemId",
                table: "SupplierReturns",
                column: "ReceivingItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierReturns_ReceivingItems_ReceivingItemId",
                table: "SupplierReturns",
                column: "ReceivingItemId",
                principalTable: "ReceivingItems",
                principalColumn: "ReceivingItemId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupplierReturns_ReceivingItems_ReceivingItemId",
                table: "SupplierReturns");

            migrationBuilder.DropIndex(
                name: "IX_SupplierReturns_ReceivingItemId",
                table: "SupplierReturns");

            migrationBuilder.AddColumn<decimal>(
                name: "CreditAmount",
                table: "SupplierReturns",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
