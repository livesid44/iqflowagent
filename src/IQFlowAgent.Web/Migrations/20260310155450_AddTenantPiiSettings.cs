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
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<int>(nullable: false),
                    IsEnabled = table.Column<bool>(nullable: false),
                    BlockOnDetection = table.Column<bool>(nullable: false),
                    DetectEmailAddresses = table.Column<bool>(nullable: false),
                    DetectPhoneNumbers = table.Column<bool>(nullable: false),
                    DetectCreditCardNumbers = table.Column<bool>(nullable: false),
                    DetectSsnNumbers = table.Column<bool>(nullable: false),
                    DetectIpAddresses = table.Column<bool>(nullable: false),
                    DetectPassportNumbers = table.Column<bool>(nullable: false),
                    DetectDatesOfBirth = table.Column<bool>(nullable: false),
                    DetectUrls = table.Column<bool>(nullable: false),
                    DetectPersonNames = table.Column<bool>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedByUserId = table.Column<string>(nullable: true)
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
