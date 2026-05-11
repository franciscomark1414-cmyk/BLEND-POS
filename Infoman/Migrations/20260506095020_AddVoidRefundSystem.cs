using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infoman.Migrations
{
    /// <inheritdoc />
    public partial class AddVoidRefundSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProductId",
                table: "WasteLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAt",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidedBy",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRefunded",
                table: "OrderItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsVoided",
                table: "OrderItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RefundReason",
                table: "OrderItems",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "OrderItems",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_WasteLogs_ProductId",
                table: "WasteLogs",
                column: "ProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_WasteLogs_Products_ProductId",
                table: "WasteLogs",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WasteLogs_Products_ProductId",
                table: "WasteLogs");

            migrationBuilder.DropIndex(
                name: "IX_WasteLogs_ProductId",
                table: "WasteLogs");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "WasteLogs");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "VoidedBy",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsRefunded",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "IsVoided",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "RefundReason",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "OrderItems");
        }
    }
}
