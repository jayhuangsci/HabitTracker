using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PromptCrew.Api.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeActivityPaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Rounds_ProjectId_EmployeeId",
                table: "Rounds");

            migrationBuilder.CreateIndex(
                name: "IX_Rounds_ProjectId_EmployeeId_CreatedUtc_Id",
                table: "Rounds",
                columns: new[] { "ProjectId", "EmployeeId", "CreatedUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Rounds_ProjectId_EmployeeId_CreatedUtc_Id",
                table: "Rounds");

            migrationBuilder.CreateIndex(
                name: "IX_Rounds_ProjectId_EmployeeId",
                table: "Rounds",
                columns: new[] { "ProjectId", "EmployeeId" });
        }
    }
}
