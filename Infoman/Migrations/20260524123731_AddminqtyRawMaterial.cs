using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infoman.Migrations
{
    /// <inheritdoc />
    public partial class AddminqtyRawMaterial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MinQtyInBaseUnit",
                table: "RawMaterials",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinQtyInBaseUnit",
                table: "RawMaterials");
        }
    }
}
