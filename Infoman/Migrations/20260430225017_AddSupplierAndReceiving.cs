using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infoman.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierAndReceiving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    SupplierId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContactPerson = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.SupplierId);
                });

            migrationBuilder.CreateTable(
                name: "Receivings",
                columns: table => new
                {
                    ReceivingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    DRNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DateReceived = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DRImagePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receivings", x => x.ReceivingId);
                    table.ForeignKey(
                        name: "FK_Receivings_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "SupplierId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReceivingItems",
                columns: table => new
                {
                    ReceivingItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReceivingId = table.Column<int>(type: "int", nullable: false),
                    RawMaterialId = table.Column<int>(type: "int", nullable: false),
                    OrderedQty = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ReceivedQty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AcceptedQty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RejectedQty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RejectionReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingItems", x => x.ReceivingItemId);
                    table.ForeignKey(
                        name: "FK_ReceivingItems_RawMaterials_RawMaterialId",
                        column: x => x.RawMaterialId,
                        principalTable: "RawMaterials",
                        principalColumn: "RawMaterialId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReceivingItems_Receivings_ReceivingId",
                        column: x => x.ReceivingId,
                        principalTable: "Receivings",
                        principalColumn: "ReceivingId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingItems_RawMaterialId",
                table: "ReceivingItems",
                column: "RawMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingItems_ReceivingId",
                table: "ReceivingItems",
                column: "ReceivingId");

            migrationBuilder.CreateIndex(
                name: "IX_Receivings_SupplierId",
                table: "Receivings",
                column: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReceivingItems");

            migrationBuilder.DropTable(
                name: "Receivings");

            migrationBuilder.DropTable(
                name: "Suppliers");
        }
    }
}
