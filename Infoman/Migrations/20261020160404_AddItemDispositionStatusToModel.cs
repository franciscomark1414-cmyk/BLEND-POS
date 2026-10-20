using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infoman.Migrations
{
    /// <inheritdoc />
    public partial class AddItemDispositionStatusToModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemDispositionStatus",
                columns: table => new
                {
                    DispositionId = table.Column<int>(type: "int", nullable: false),
                    StatusName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemDispositionStatus", x => x.DispositionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_DispositionStatusId",
                table: "OrderItems",
                column: "DispositionStatusId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_ItemDispositionStatus_DispositionStatusId",
                table: "OrderItems",
                column: "DispositionStatusId",
                principalTable: "ItemDispositionStatus",
                principalColumn: "DispositionId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_ItemDispositionStatus_DispositionStatusId",
                table: "OrderItems");

            migrationBuilder.DropTable(
                name: "ItemDispositionStatus");

            migrationBuilder.DropIndex(
                name: "IX_OrderItems_DispositionStatusId",
                table: "OrderItems");
        }
    }
}
