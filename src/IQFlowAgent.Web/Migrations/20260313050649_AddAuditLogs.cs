using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IQFlowAgent.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<int>(type: "INTEGER", nullable: false),
                    IntakeRecordId = table.Column<int>(type: "INTEGER", nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", nullable: false),
                    CallSite = table.Column<string>(type: "TEXT", nullable: false),
                    PiiScanStatus = table.Column<string>(type: "TEXT", nullable: false),
                    PiiFindingsJson = table.Column<string>(type: "TEXT", nullable: true),
                    PiiFindingCount = table.Column<int>(type: "INTEGER", nullable: false),
                    WasBlocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequestUrl = table.Column<string>(type: "TEXT", nullable: true),
                    HttpStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: true),
                    IsMocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");
        }
    }
}
