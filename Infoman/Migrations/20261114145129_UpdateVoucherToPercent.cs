using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infoman.Migrations
{
    /// <inheritdoc />
    public partial class UpdateVoucherToPercent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DiscountAmount",
                table: "Vouchers",
                newName: "DiscountPercent");

            migrationBuilder.RenameColumn(
                name: "VoucherDiscount",
                table: "Orders",
                newName: "VoucherDiscountPercent");

            migrationBuilder.AddColumn<decimal>(
                name: "VoucherDiscountAmount",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VoucherDiscountAmount",
                table: "Orders");

            migrationBuilder.RenameColumn(
                name: "DiscountPercent",
                table: "Vouchers",
                newName: "DiscountAmount");

            migrationBuilder.RenameColumn(
                name: "VoucherDiscountPercent",
                table: "Orders",
                newName: "VoucherDiscount");
        }
    }
}
