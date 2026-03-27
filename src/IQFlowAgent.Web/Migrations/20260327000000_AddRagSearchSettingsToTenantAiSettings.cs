using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IQFlowAgent.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddRagSearchSettingsToTenantAiSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AzureDocumentIntelligenceEndpoint",
                table: "TenantAiSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AzureDocumentIntelligenceApiKey",
                table: "TenantAiSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AzureOpenAIEmbeddingDeployment",
                table: "TenantAiSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "text-embedding-3-small");

            migrationBuilder.AddColumn<string>(
                name: "AzureSearchEndpoint",
                table: "TenantAiSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AzureSearchApiKey",
                table: "TenantAiSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AzureSearchIndexName",
                table: "TenantAiSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "iqflow-rag-chunks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AzureDocumentIntelligenceEndpoint",
                table: "TenantAiSettings");

            migrationBuilder.DropColumn(
                name: "AzureDocumentIntelligenceApiKey",
                table: "TenantAiSettings");

            migrationBuilder.DropColumn(
                name: "AzureOpenAIEmbeddingDeployment",
                table: "TenantAiSettings");

            migrationBuilder.DropColumn(
                name: "AzureSearchEndpoint",
                table: "TenantAiSettings");

            migrationBuilder.DropColumn(
                name: "AzureSearchApiKey",
                table: "TenantAiSettings");

            migrationBuilder.DropColumn(
                name: "AzureSearchIndexName",
                table: "TenantAiSettings");
        }
    }
}
