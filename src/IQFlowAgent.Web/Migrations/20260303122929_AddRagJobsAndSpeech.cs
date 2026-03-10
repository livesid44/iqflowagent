using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IQFlowAgent.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddRagJobsAndSpeech : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AzureSpeechApiKey",
                table: "TenantAiSettings",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AzureSpeechRegion",
                table: "TenantAiSettings",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SopDocumentPath",
                table: "IntakeDocuments",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TranscriptStatus",
                table: "IntakeDocuments",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TranscriptText",
                table: "IntakeDocuments",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RagJobs",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IntakeRecordId = table.Column<int>(nullable: false),
                    Status = table.Column<string>(nullable: false),
                    TotalFiles = table.Column<int>(nullable: false),
                    ProcessedFiles = table.Column<int>(nullable: false),
                    ErrorMessage = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    StartedAt = table.Column<DateTime>(nullable: true),
                    CompletedAt = table.Column<DateTime>(nullable: true),
                    NotifyUserId = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RagJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RagJobs_IntakeRecords_IntakeRecordId",
                        column: x => x.IntakeRecordId,
                        principalTable: "IntakeRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RagJobs_IntakeRecordId",
                table: "RagJobs",
                column: "IntakeRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RagJobs");

            migrationBuilder.DropColumn(
                name: "AzureSpeechApiKey",
                table: "TenantAiSettings");

            migrationBuilder.DropColumn(
                name: "AzureSpeechRegion",
                table: "TenantAiSettings");

            migrationBuilder.DropColumn(
                name: "SopDocumentPath",
                table: "IntakeDocuments");

            migrationBuilder.DropColumn(
                name: "TranscriptStatus",
                table: "IntakeDocuments");

            migrationBuilder.DropColumn(
                name: "TranscriptText",
                table: "IntakeDocuments");
        }
    }
}
