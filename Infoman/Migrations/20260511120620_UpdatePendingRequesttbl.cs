using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infoman.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePendingRequesttbl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PendingRequests_ApprovedBy",
                table: "PendingRequests",
                column: "ApprovedBy");

            migrationBuilder.AddForeignKey(
                name: "FK_PendingRequests_Users_ApprovedBy",
                table: "PendingRequests",
                column: "ApprovedBy",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PendingRequests_Users_ApprovedBy",
                table: "PendingRequests");

            migrationBuilder.DropIndex(
                name: "IX_PendingRequests_ApprovedBy",
                table: "PendingRequests");
        }
    }
}
