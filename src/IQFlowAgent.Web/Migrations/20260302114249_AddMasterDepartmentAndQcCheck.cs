using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IQFlowAgent.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddMasterDepartmentAndQcCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MasterDepartments",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(nullable: false),
                    Description = table.Column<string>(nullable: true),
                    IsActive = table.Column<bool>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasterDepartments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QcChecks",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Sqlite:Autoincrement", true),
                    IntakeRecordId = table.Column<int>(nullable: false),
                    OverallScore = table.Column<int>(nullable: false),
                    ScoreBreakdownJson = table.Column<string>(nullable: true),
                    Status = table.Column<string>(nullable: false),
                    ErrorMessage = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CompletedAt = table.Column<DateTime>(nullable: true),
                    RunByUserId = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QcChecks_IntakeRecords_IntakeRecordId",
                        column: x => x.IntakeRecordId,
                        principalTable: "IntakeRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QcChecks_IntakeRecordId",
                table: "QcChecks",
                column: "IntakeRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MasterDepartments");

            migrationBuilder.DropTable(
                name: "QcChecks");
        }
    }
}
