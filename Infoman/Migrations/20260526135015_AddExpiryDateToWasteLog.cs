using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infoman.Migrations
{
    /// <inheritdoc />
    public partial class AddExpiryDateToWasteLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryDate",
                table: "WasteLogs",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpiryDate",
                table: "WasteLogs");
        }
    }
}
