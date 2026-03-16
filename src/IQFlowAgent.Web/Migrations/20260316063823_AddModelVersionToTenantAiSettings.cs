using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IQFlowAgent.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddModelVersionToTenantAiSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AzureOpenAIModelVersion",
                table: "TenantAiSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "gpt-5.2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AzureOpenAIModelVersion",
                table: "TenantAiSettings");
        }
    }
}
