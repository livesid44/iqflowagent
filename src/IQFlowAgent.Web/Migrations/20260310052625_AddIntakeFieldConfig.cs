using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IQFlowAgent.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddIntakeFieldConfig : Migration
    {
        // Seed data — one row per configurable intake field for TenantId = 1.
        // (FieldName, DisplayName, SectionName, IsMandatory, DisplayOrder)
        private static readonly (string F, string D, string S, bool M, int O)[] Seed =
        [
            ("ProcessName",           "Process Name",           "Process Information",      true,  1),
            ("Description",           "Description",            "Process Information",      true,  2),
            ("ProcessType",           "Process Type",           "Process Information",      false, 3),
            ("Priority",              "Priority",               "Process Information",      false, 4),
            ("EstimatedVolumePerDay", "Est. Volume / Day",      "Process Information",      false, 5),
            ("BusinessUnit",          "Business Unit",          "Ownership & Organisation", false, 6),
            ("Department",            "Department",             "Ownership & Organisation", false, 7),
            ("Lob",                   "Line of Business (LOB)", "Ownership & Organisation", false, 8),
            ("SdcLots",               "Lots or SDC",            "Ownership & Organisation", false, 9),
            ("ProcessOwnerName",      "Process Owner Name",     "Ownership & Organisation", false, 10),
            ("ProcessOwnerEmail",     "Process Owner Email",    "Ownership & Organisation", false, 11),
            ("Country",               "Country",                "Location",                 false, 12),
            ("City",                  "City",                   "Location",                 false, 13),
            ("SiteLocation",          "Site / Office Location", "Location",                 false, 14),
            ("TimeZone",              "Time Zone",              "Location",                 false, 15),
            ("Document",              "Document Upload",        "Documents",                false, 16),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IntakeFieldConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<int>(type: "INTEGER", nullable: false),
                    FieldName = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    SectionName = table.Column<string>(type: "TEXT", nullable: false),
                    IsVisible = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsMandatory = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeFieldConfigs", x => x.Id);
                });

            // Seed default config for TenantId = 1 (all fields visible)
            foreach (var (fieldName, displayName, sectionName, isMandatory, displayOrder) in Seed)
            {
                migrationBuilder.InsertData(
                    table: "IntakeFieldConfigs",
                    columns: ["TenantId", "FieldName", "DisplayName", "SectionName", "IsVisible", "IsMandatory", "DisplayOrder"],
                    values: new object[] { 1, fieldName, displayName, sectionName, true, isMandatory, displayOrder });
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntakeFieldConfigs");
        }
    }
}

