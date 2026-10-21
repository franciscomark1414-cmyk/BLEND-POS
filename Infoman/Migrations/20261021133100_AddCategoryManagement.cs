using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infoman.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Categories table
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    CategoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_Categories", x => x.CategoryId));

            // 2. Default categories
            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "CategoryId", "CreatedAt", "IsActive", "Name", "Type" },
                values: new object[,]
                {
            { 1, new DateTime(2026, 1, 1), true, "Fruits",      "RawMaterial" },
            { 2, new DateTime(2026, 1, 1), true, "Dairy",       "RawMaterial" },
            { 3, new DateTime(2026, 1, 1), true, "Others",      "RawMaterial" },
            { 4, new DateTime(2026, 1, 1), true, "Sweeteners",  "RawMaterial" },
            { 5, new DateTime(2026, 1, 1), true, "Toppings",    "RawMaterial" },
            { 6, new DateTime(2026, 1, 1), true, "Fruit Shake", "Product" },
            { 7, new DateTime(2026, 1, 1), true, "Milk Shake",  "Product" },
            { 8, new DateTime(2026, 1, 1), true, "Specialty",   "Product" }
                });

            // 3. Bagong FK column sa RawMaterials
            migrationBuilder.AddColumn<int>(name: "CategoryId", table: "RawMaterials", type: "int", nullable: true);

            // 4. BACKFILL: i-convert ang lumang enum value (0-4) -> CategoryId (1-5)
            migrationBuilder.Sql("UPDATE RawMaterials SET CategoryId = [Category] + 1");

            // 5. I-import ang mga dating free-text na Product category (hal. custom na tinype mo dati)
            migrationBuilder.Sql(@"
        INSERT INTO Categories (Name, Type, IsActive, CreatedAt)
        SELECT DISTINCT p.Category, 'Product', 1, GETDATE()
        FROM Products p
        WHERE p.Category <> ''
          AND NOT EXISTS (SELECT 1 FROM Categories c WHERE c.Type = 'Product' AND c.Name = p.Category)");

            // 6. Saka lang i-drop ang lumang enum column
            migrationBuilder.DropColumn(name: "Category", table: "RawMaterials");

            // 7. Indexes at foreign key
            migrationBuilder.CreateIndex(name: "IX_Categories_Type_Name", table: "Categories",
                columns: new[] { "Type", "Name" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_RawMaterials_CategoryId", table: "RawMaterials", column: "CategoryId");
            migrationBuilder.AddForeignKey(
                name: "FK_RawMaterials_Categories_CategoryId", table: "RawMaterials", column: "CategoryId",
                principalTable: "Categories", principalColumn: "CategoryId", onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_RawMaterials_Categories_CategoryId", table: "RawMaterials");
            migrationBuilder.DropIndex(name: "IX_RawMaterials_CategoryId", table: "RawMaterials");

            migrationBuilder.AddColumn<int>(name: "Category", table: "RawMaterials", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.Sql("UPDATE RawMaterials SET Category = CASE WHEN CategoryId BETWEEN 1 AND 5 THEN CategoryId - 1 ELSE 2 END");

            migrationBuilder.DropColumn(name: "CategoryId", table: "RawMaterials");
            migrationBuilder.DropTable(name: "Categories");
        }
    }
}
