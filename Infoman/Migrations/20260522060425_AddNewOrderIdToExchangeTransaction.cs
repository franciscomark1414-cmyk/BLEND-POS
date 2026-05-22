using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infoman.Migrations
{
    /// <inheritdoc />
    public partial class AddNewOrderIdToExchangeTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AdjustmentAmount",
                table: "ExchangeTransactions",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "AdjustmentType",
                table: "ExchangeTransactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NewOrderId",
                table: "ExchangeTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalAmount",
                table: "ExchangeTransactions",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "ExchangeTransactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReplacementAmount",
                table: "ExchangeTransactions",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WasteCost",
                table: "ExchangeTransactions",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeTransactions_NewOrderId",
                table: "ExchangeTransactions",
                column: "NewOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExchangeTransactions_Orders_NewOrderId",
                table: "ExchangeTransactions",
                column: "NewOrderId",
                principalTable: "Orders",
                principalColumn: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExchangeTransactions_Orders_NewOrderId",
                table: "ExchangeTransactions");

            migrationBuilder.DropIndex(
                name: "IX_ExchangeTransactions_NewOrderId",
                table: "ExchangeTransactions");

            migrationBuilder.DropColumn(
                name: "AdjustmentAmount",
                table: "ExchangeTransactions");

            migrationBuilder.DropColumn(
                name: "AdjustmentType",
                table: "ExchangeTransactions");

            migrationBuilder.DropColumn(
                name: "NewOrderId",
                table: "ExchangeTransactions");

            migrationBuilder.DropColumn(
                name: "OriginalAmount",
                table: "ExchangeTransactions");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "ExchangeTransactions");

            migrationBuilder.DropColumn(
                name: "ReplacementAmount",
                table: "ExchangeTransactions");

            migrationBuilder.DropColumn(
                name: "WasteCost",
                table: "ExchangeTransactions");
        }
    }
}
