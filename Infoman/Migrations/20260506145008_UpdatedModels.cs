using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infoman.Migrations
{
    /// <inheritdoc />
    public partial class UpdatedModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_inventoryMovements_RawMaterials_RawMaterialId",
                table: "inventoryMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_refunds_Orders_TransactionId",
                table: "refunds");

            migrationBuilder.DropForeignKey(
                name: "FK_voidLogs_OrderItems_ItemId",
                table: "voidLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_voidLogs_Orders_TransactionId",
                table: "voidLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_voidLogs_Users_ApprovedBy",
                table: "voidLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_voidLogs_Users_UserId",
                table: "voidLogs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_voidLogs",
                table: "voidLogs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_refunds",
                table: "refunds");

            migrationBuilder.DropPrimaryKey(
                name: "PK_inventoryMovements",
                table: "inventoryMovements");

            migrationBuilder.RenameTable(
                name: "voidLogs",
                newName: "VoidLogs");

            migrationBuilder.RenameTable(
                name: "refunds",
                newName: "Refunds");

            migrationBuilder.RenameTable(
                name: "inventoryMovements",
                newName: "InventoryMovements");

            migrationBuilder.RenameIndex(
                name: "IX_voidLogs_UserId",
                table: "VoidLogs",
                newName: "IX_VoidLogs_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_voidLogs_TransactionId",
                table: "VoidLogs",
                newName: "IX_VoidLogs_TransactionId");

            migrationBuilder.RenameIndex(
                name: "IX_voidLogs_ItemId",
                table: "VoidLogs",
                newName: "IX_VoidLogs_ItemId");

            migrationBuilder.RenameIndex(
                name: "IX_voidLogs_ApprovedBy",
                table: "VoidLogs",
                newName: "IX_VoidLogs_ApprovedBy");

            migrationBuilder.RenameIndex(
                name: "IX_refunds_TransactionId",
                table: "Refunds",
                newName: "IX_Refunds_TransactionId");

            migrationBuilder.RenameIndex(
                name: "IX_inventoryMovements_RawMaterialId",
                table: "InventoryMovements",
                newName: "IX_InventoryMovements_RawMaterialId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_VoidLogs",
                table: "VoidLogs",
                column: "VoidLogId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Refunds",
                table: "Refunds",
                column: "RefundId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_InventoryMovements",
                table: "InventoryMovements",
                column: "InventoryMovementId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_RawMaterials_RawMaterialId",
                table: "InventoryMovements",
                column: "RawMaterialId",
                principalTable: "RawMaterials",
                principalColumn: "RawMaterialId");

            migrationBuilder.AddForeignKey(
                name: "FK_Refunds_Orders_TransactionId",
                table: "Refunds",
                column: "TransactionId",
                principalTable: "Orders",
                principalColumn: "OrderId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VoidLogs_OrderItems_ItemId",
                table: "VoidLogs",
                column: "ItemId",
                principalTable: "OrderItems",
                principalColumn: "OrderItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_VoidLogs_Orders_TransactionId",
                table: "VoidLogs",
                column: "TransactionId",
                principalTable: "Orders",
                principalColumn: "OrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_VoidLogs_Users_ApprovedBy",
                table: "VoidLogs",
                column: "ApprovedBy",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_VoidLogs_Users_UserId",
                table: "VoidLogs",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_RawMaterials_RawMaterialId",
                table: "InventoryMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_Refunds_Orders_TransactionId",
                table: "Refunds");

            migrationBuilder.DropForeignKey(
                name: "FK_VoidLogs_OrderItems_ItemId",
                table: "VoidLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_VoidLogs_Orders_TransactionId",
                table: "VoidLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_VoidLogs_Users_ApprovedBy",
                table: "VoidLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_VoidLogs_Users_UserId",
                table: "VoidLogs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_VoidLogs",
                table: "VoidLogs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Refunds",
                table: "Refunds");

            migrationBuilder.DropPrimaryKey(
                name: "PK_InventoryMovements",
                table: "InventoryMovements");

            migrationBuilder.RenameTable(
                name: "VoidLogs",
                newName: "voidLogs");

            migrationBuilder.RenameTable(
                name: "Refunds",
                newName: "refunds");

            migrationBuilder.RenameTable(
                name: "InventoryMovements",
                newName: "inventoryMovements");

            migrationBuilder.RenameIndex(
                name: "IX_VoidLogs_UserId",
                table: "voidLogs",
                newName: "IX_voidLogs_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_VoidLogs_TransactionId",
                table: "voidLogs",
                newName: "IX_voidLogs_TransactionId");

            migrationBuilder.RenameIndex(
                name: "IX_VoidLogs_ItemId",
                table: "voidLogs",
                newName: "IX_voidLogs_ItemId");

            migrationBuilder.RenameIndex(
                name: "IX_VoidLogs_ApprovedBy",
                table: "voidLogs",
                newName: "IX_voidLogs_ApprovedBy");

            migrationBuilder.RenameIndex(
                name: "IX_Refunds_TransactionId",
                table: "refunds",
                newName: "IX_refunds_TransactionId");

            migrationBuilder.RenameIndex(
                name: "IX_InventoryMovements_RawMaterialId",
                table: "inventoryMovements",
                newName: "IX_inventoryMovements_RawMaterialId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_voidLogs",
                table: "voidLogs",
                column: "VoidLogId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_refunds",
                table: "refunds",
                column: "RefundId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_inventoryMovements",
                table: "inventoryMovements",
                column: "InventoryMovementId");

            migrationBuilder.AddForeignKey(
                name: "FK_inventoryMovements_RawMaterials_RawMaterialId",
                table: "inventoryMovements",
                column: "RawMaterialId",
                principalTable: "RawMaterials",
                principalColumn: "RawMaterialId");

            migrationBuilder.AddForeignKey(
                name: "FK_refunds_Orders_TransactionId",
                table: "refunds",
                column: "TransactionId",
                principalTable: "Orders",
                principalColumn: "OrderId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_voidLogs_OrderItems_ItemId",
                table: "voidLogs",
                column: "ItemId",
                principalTable: "OrderItems",
                principalColumn: "OrderItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_voidLogs_Orders_TransactionId",
                table: "voidLogs",
                column: "TransactionId",
                principalTable: "Orders",
                principalColumn: "OrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_voidLogs_Users_ApprovedBy",
                table: "voidLogs",
                column: "ApprovedBy",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_voidLogs_Users_UserId",
                table: "voidLogs",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
