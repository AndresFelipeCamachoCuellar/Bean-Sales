using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Web.Migrations
{
    /// <inheritdoc />
    public partial class FixParametricPermissionIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ParametricPermissions_Code",
                table: "ParametricPermissions");

            migrationBuilder.DropIndex(
                name: "IX_ParametricPermissions_ModuleID",
                table: "ParametricPermissions");

            migrationBuilder.CreateIndex(
                name: "IX_ParametricPermissions_ModuleID_Code",
                table: "ParametricPermissions",
                columns: new[] { "ModuleID", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ParametricPermissions_ModuleID_Code",
                table: "ParametricPermissions");

            migrationBuilder.CreateIndex(
                name: "IX_ParametricPermissions_Code",
                table: "ParametricPermissions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParametricPermissions_ModuleID",
                table: "ParametricPermissions",
                column: "ModuleID");
        }
    }
}
