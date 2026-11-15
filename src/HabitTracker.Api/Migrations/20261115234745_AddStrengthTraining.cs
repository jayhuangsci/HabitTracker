using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HabitTracker.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStrengthTraining : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Workouts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workouts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkoutSets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkoutSessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    ClientId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExerciseId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Unit = table.Column<int>(type: "INTEGER", nullable: false),
                    BarWeight = table.Column<decimal>(type: "TEXT", nullable: false),
                    LeftPlatesJson = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    RightPlatesJson = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Reps = table.Column<int>(type: "INTEGER", nullable: false),
                    Rpe = table.Column<decimal>(type: "TEXT", nullable: true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutSets", x => x.Id);
                    table.CheckConstraint("CK_WorkoutSets_Reps", "\"Reps\" BETWEEN 1 AND 100");
                    table.ForeignKey(
                        name: "FK_WorkoutSets_Workouts_WorkoutSessionId",
                        column: x => x.WorkoutSessionId,
                        principalTable: "Workouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Workouts_Date",
                table: "Workouts",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSets_ClientId",
                table: "WorkoutSets",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSets_ExerciseId",
                table: "WorkoutSets",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSets_WorkoutSessionId",
                table: "WorkoutSets",
                column: "WorkoutSessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkoutSets");

            migrationBuilder.DropTable(
                name: "Workouts");
        }
    }
}
