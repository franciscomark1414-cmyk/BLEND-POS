using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infoman.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceAndAttendanceTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeAttendance_Users_UserId",
                table: "EmployeeAttendance");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeePerformance_Users_UserId",
                table: "EmployeePerformance");

            migrationBuilder.DropPrimaryKey(
                name: "PK_EmployeePerformance",
                table: "EmployeePerformance");

            migrationBuilder.DropPrimaryKey(
                name: "PK_EmployeeAttendance",
                table: "EmployeeAttendance");

            migrationBuilder.RenameTable(
                name: "EmployeePerformance",
                newName: "EmployeePerformances");

            migrationBuilder.RenameTable(
                name: "EmployeeAttendance",
                newName: "EmployeeAttendances");

            migrationBuilder.RenameIndex(
                name: "IX_EmployeePerformance_UserId",
                table: "EmployeePerformances",
                newName: "IX_EmployeePerformances_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_EmployeeAttendance_UserId",
                table: "EmployeeAttendances",
                newName: "IX_EmployeeAttendances_UserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_EmployeePerformances",
                table: "EmployeePerformances",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_EmployeeAttendances",
                table: "EmployeeAttendances",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeAttendances_Users_UserId",
                table: "EmployeeAttendances",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeePerformances_Users_UserId",
                table: "EmployeePerformances",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeAttendances_Users_UserId",
                table: "EmployeeAttendances");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeePerformances_Users_UserId",
                table: "EmployeePerformances");

            migrationBuilder.DropPrimaryKey(
                name: "PK_EmployeePerformances",
                table: "EmployeePerformances");

            migrationBuilder.DropPrimaryKey(
                name: "PK_EmployeeAttendances",
                table: "EmployeeAttendances");

            migrationBuilder.RenameTable(
                name: "EmployeePerformances",
                newName: "EmployeePerformance");

            migrationBuilder.RenameTable(
                name: "EmployeeAttendances",
                newName: "EmployeeAttendance");

            migrationBuilder.RenameIndex(
                name: "IX_EmployeePerformances_UserId",
                table: "EmployeePerformance",
                newName: "IX_EmployeePerformance_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_EmployeeAttendances_UserId",
                table: "EmployeeAttendance",
                newName: "IX_EmployeeAttendance_UserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_EmployeePerformance",
                table: "EmployeePerformance",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_EmployeeAttendance",
                table: "EmployeeAttendance",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeAttendance_Users_UserId",
                table: "EmployeeAttendance",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeePerformance_Users_UserId",
                table: "EmployeePerformance",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
