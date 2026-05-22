using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infoman.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreCreditLogic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AdjustmentAmount",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "AdjustmentType",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExchangeDate",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeReason",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExchangeTransactionId",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginalOrderId",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnStatus",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StoreCreditApplied",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ExchangeTransactionId",
                table: "OrderItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsExchanged",
                table: "OrderItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "QuantityExchanged",
                table: "OrderItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OriginalOrderId",
                table: "Orders",
                column: "OriginalOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Orders_OriginalOrderId",
                table: "Orders",
                column: "OriginalOrderId",
                principalTable: "Orders",
                principalColumn: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Orders_OriginalOrderId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_OriginalOrderId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AdjustmentAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AdjustmentType",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ExchangeDate",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ExchangeReason",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ExchangeTransactionId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "OriginalOrderId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ReturnStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "StoreCreditApplied",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ExchangeTransactionId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "IsExchanged",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "QuantityExchanged",
                table: "OrderItems");
        }
    }
}
