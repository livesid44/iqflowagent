using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IQFlowAgent.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantPiiSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantPiiSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    BlockOnDetection = table.Column<bool>(type: "INTEGER", nullable: false),
                    DetectEmailAddresses = table.Column<bool>(type: "INTEGER", nullable: false),
                    DetectPhoneNumbers = table.Column<bool>(type: "INTEGER", nullable: false),
                    DetectCreditCardNumbers = table.Column<bool>(type: "INTEGER", nullable: false),
                    DetectSsnNumbers = table.Column<bool>(type: "INTEGER", nullable: false),
                    DetectIpAddresses = table.Column<bool>(type: "INTEGER", nullable: false),
                    DetectPassportNumbers = table.Column<bool>(type: "INTEGER", nullable: false),
                    DetectDatesOfBirth = table.Column<bool>(type: "INTEGER", nullable: false),
                    DetectUrls = table.Column<bool>(type: "INTEGER", nullable: false),
                    DetectPersonNames = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantPiiSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantPiiSettings_TenantId",
                table: "TenantPiiSettings",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantPiiSettings");
        }
    }
}
