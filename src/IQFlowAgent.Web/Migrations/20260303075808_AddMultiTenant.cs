using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IQFlowAgent.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "QcChecks",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "MasterDepartments",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "IntakeRecords",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "AuthSettings",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(nullable: false),
                    Slug = table.Column<string>(nullable: false),
                    Color = table.Column<string>(nullable: false),
                    Description = table.Column<string>(nullable: true),
                    IsActive = table.Column<bool>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantAiSettings",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<int>(nullable: false),
                    AzureOpenAIEndpoint = table.Column<string>(nullable: false),
                    AzureOpenAIApiKey = table.Column<string>(nullable: false),
                    AzureOpenAIDeploymentName = table.Column<string>(nullable: false),
                    AzureOpenAIApiVersion = table.Column<string>(nullable: false),
                    AzureOpenAIMaxTokens = table.Column<int>(nullable: false),
                    AzureStorageConnectionString = table.Column<string>(nullable: false),
                    AzureStorageContainerName = table.Column<string>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedByUserId = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantAiSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantAiSettings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserTenants",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(nullable: false),
                    TenantId = table.Column<int>(nullable: false),
                    TenantRole = table.Column<string>(nullable: false),
                    IsDefault = table.Column<bool>(nullable: false),
                    AssignedAt = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTenants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserTenants_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserTenants_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantAiSettings_TenantId",
                table: "TenantAiSettings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UserTenants_TenantId",
                table: "UserTenants",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UserTenants_UserId",
                table: "UserTenants",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantAiSettings");

            migrationBuilder.DropTable(
                name: "UserTenants");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "QcChecks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "MasterDepartments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "IntakeRecords");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuthSettings");
        }
    }
}
