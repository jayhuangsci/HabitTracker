using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PromptCrew.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialPromptCrew : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevokedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Goal = table.Column<string>(type: "TEXT", nullable: false),
                    Background = table.Column<string>(type: "TEXT", nullable: false),
                    Constraints = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Chains",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Goal = table.Column<string>(type: "TEXT", nullable: false),
                    AcceptanceCriteria = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    NextNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chains", x => x.Id);
                    table.UniqueConstraint("AK_Chains_ProjectId_Id", x => new { x.ProjectId, x.Id });
                    table.ForeignKey(
                        name: "FK_Chains_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Responsibilities = table.Column<string>(type: "TEXT", nullable: false),
                    BasePrompt = table.Column<string>(type: "TEXT", nullable: false),
                    SkillsJson = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                    table.UniqueConstraint("AK_Employees_ProjectId_Id", x => new { x.ProjectId, x.Id });
                    table.ForeignKey(
                        name: "FK_Employees_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Rounds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChainId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentRoundId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RevisesRoundId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestHash = table.Column<string>(type: "TEXT", nullable: false),
                    Input = table.Column<string>(type: "TEXT", nullable: false),
                    EmployeeSnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rounds", x => x.Id);
                    table.UniqueConstraint("AK_Rounds_ProjectId_ChainId_Id", x => new { x.ProjectId, x.ChainId, x.Id });
                    table.UniqueConstraint("AK_Rounds_ProjectId_Id", x => new { x.ProjectId, x.Id });
                    table.ForeignKey(
                        name: "FK_Rounds_Chains_ProjectId_ChainId",
                        columns: x => new { x.ProjectId, x.ChainId },
                        principalTable: "Chains",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Rounds_Employees_ProjectId_EmployeeId",
                        columns: x => new { x.ProjectId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Rounds_Rounds_ProjectId_ChainId_ParentRoundId",
                        columns: x => new { x.ProjectId, x.ChainId, x.ParentRoundId },
                        principalTable: "Rounds",
                        principalColumns: new[] { "ProjectId", "ChainId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Rounds_Rounds_ProjectId_ChainId_RevisesRoundId",
                        columns: x => new { x.ProjectId, x.ChainId, x.RevisesRoundId },
                        principalTable: "Rounds",
                        principalColumns: new[] { "ProjectId", "ChainId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Results",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoundId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestHash = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Output = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ArtifactsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Results", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Results_Rounds_ProjectId_RoundId",
                        columns: x => new { x.ProjectId, x.RoundId },
                        principalTable: "Rounds",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RoundReference",
                columns: table => new
                {
                    RoundId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReferencedRoundId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoundReference", x => new { x.RoundId, x.ReferencedRoundId });
                    table.ForeignKey(
                        name: "FK_RoundReference_Rounds_ProjectId_ReferencedRoundId",
                        columns: x => new { x.ProjectId, x.ReferencedRoundId },
                        principalTable: "Rounds",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoundReference_Rounds_ProjectId_RoundId",
                        columns: x => new { x.ProjectId, x.RoundId },
                        principalTable: "Rounds",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RoundTag",
                columns: table => new
                {
                    RoundId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoundTag", x => new { x.RoundId, x.Value });
                    table.ForeignKey(
                        name: "FK_RoundTag_Rounds_RoundId",
                        column: x => x.RoundId,
                        principalTable: "Rounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessKeys_TokenHash",
                table: "AccessKeys",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_ProjectId_NormalizedName",
                table: "Employees",
                columns: new[] { "ProjectId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Results_ProjectId_ClientRequestId",
                table: "Results",
                columns: new[] { "ProjectId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Results_ProjectId_RoundId",
                table: "Results",
                columns: new[] { "ProjectId", "RoundId" });

            migrationBuilder.CreateIndex(
                name: "IX_Results_RoundId_Version",
                table: "Results",
                columns: new[] { "RoundId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoundReference_ProjectId_ReferencedRoundId",
                table: "RoundReference",
                columns: new[] { "ProjectId", "ReferencedRoundId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoundReference_ProjectId_RoundId",
                table: "RoundReference",
                columns: new[] { "ProjectId", "RoundId" });

            migrationBuilder.CreateIndex(
                name: "IX_Rounds_ChainId_Number",
                table: "Rounds",
                columns: new[] { "ChainId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rounds_ProjectId_ChainId_ParentRoundId",
                table: "Rounds",
                columns: new[] { "ProjectId", "ChainId", "ParentRoundId" });

            migrationBuilder.CreateIndex(
                name: "IX_Rounds_ProjectId_ChainId_RevisesRoundId",
                table: "Rounds",
                columns: new[] { "ProjectId", "ChainId", "RevisesRoundId" });

            migrationBuilder.CreateIndex(
                name: "IX_Rounds_ProjectId_ClientRequestId",
                table: "Rounds",
                columns: new[] { "ProjectId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rounds_ProjectId_EmployeeId",
                table: "Rounds",
                columns: new[] { "ProjectId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoundTag_Value",
                table: "RoundTag",
                column: "Value");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessKeys");

            migrationBuilder.DropTable(
                name: "Results");

            migrationBuilder.DropTable(
                name: "RoundReference");

            migrationBuilder.DropTable(
                name: "RoundTag");

            migrationBuilder.DropTable(
                name: "Rounds");

            migrationBuilder.DropTable(
                name: "Chains");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
