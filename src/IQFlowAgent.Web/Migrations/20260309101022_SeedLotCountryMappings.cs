using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IQFlowAgent.Web.Migrations
{
    /// <inheritdoc />
    public partial class SeedLotCountryMappings : Migration
    {
        // ── Seed data ────────────────────────────────────────────────────────────
        // LOT names must match SdcLotOptions in MasterDataController exactly.
        // Cities column is a comma-separated list; empty string means "all cities".
        // TenantId = 1 (default / first tenant).
        // ─────────────────────────────────────────────────────────────────────────

        private const string Lot1 = "Lot 1 \u2013 Global Customer Support";
        private const string Lot2 = "Lot 2 \u2013 Quote to Bill";
        private const string Lot4 = "Lot 4 \u2013 One Post Sales";

        private static readonly (string LotName, string Country, string Cities)[] SeedRows =
        [
            // ── Lot 1 ─────────────────────────────────────────────────────────
            (Lot1, "Brazil",        "Petropolis"),
            (Lot1, "China",         "Beijing"),
            (Lot1, "Egypt",         "Cairo"),
            (Lot1, "Greece",        "Kallithea Attica"),
            (Lot1, "India",         "Mumbai,New Delhi"),
            (Lot1, "Italy",         "Milan,Rome"),
            (Lot1, "Malaysia",      "Kuala Lumpur"),
            (Lot1, "Romania",       "Bucharest"),
            (Lot1, "Turkey",        "Istanbul"),
            (Lot1, "United States", "Atlanta,Phoenix"),

            // ── Lot 2 ─────────────────────────────────────────────────────────
            (Lot2, "Brazil",                "Petropolis"),
            (Lot2, "Chile",                 "Santiago de Chile"),
            (Lot2, "Czech Republic",        "Prague"),
            (Lot2, "Egypt",                 "Cairo"),
            (Lot2, "Germany",               "Frankfurt"),
            (Lot2, "Greece",                "Kallithea Attica"),
            (Lot2, "Hong Kong",             "Quarry Bay"),
            (Lot2, "India",                 "Bangalore,Hyderabad,Mumbai,New Delhi"),
            (Lot2, "Italy",                 "Milan,Rome"),
            (Lot2, "Lebanon",               "Beirut"),
            (Lot2, "Malaysia",              "Kuala Lumpur"),
            (Lot2, "Netherlands",           "Amsterdam"),
            (Lot2, "Portugal",              "Amadora"),
            (Lot2, "Romania",               "Bucharest"),
            (Lot2, "Slovakia",              "Bratislava"),
            (Lot2, "South Africa",          "Johannesburg"),
            (Lot2, "Spain",                 "Barcelona,Madrid"),
            (Lot2, "Turkey",                "Istanbul"),
            (Lot2, "United Arab Emirates",  "Dubai"),
            (Lot2, "United Kingdom",        "Slough"),
            (Lot2, "United States",         "Atlanta"),

            // ── Lot 4 ─────────────────────────────────────────────────────────
            (Lot4, "Argentina",             "Buenos Aires"),
            (Lot4, "Australia",             "Melbourne,Sydney,Perth"),
            (Lot4, "Austria",               "Vienna"),
            (Lot4, "Belgium",               "Brussels"),
            (Lot4, "Brazil",                "Indaial,Petropolis,Porto Alegre,Sao Paulo"),
            (Lot4, "Canada",                "Montreal,Toronto,Vancouver"),
            (Lot4, "Chile",                 "Santiago de Chile"),
            (Lot4, "China",                 "Beijing,Guangzhou,Shanghai,Shenzhen"),
            (Lot4, "Colombia",              "Bogota"),
            (Lot4, "Czech Republic",        "Prague"),
            (Lot4, "Denmark",               "Copenhagen"),
            (Lot4, "Egypt",                 "Cairo"),
            (Lot4, "Finland",               "Helsinki"),
            (Lot4, "Germany",               "Duesseldorf,Frankfurt,Hamburg,Munich,Stuttgart"),
            (Lot4, "Greece",                "Kallithea Attica"),
            (Lot4, "Hong Kong",             "Quarry Bay,Wanchai"),
            (Lot4, "Hungary",               "Bucharest"),
            (Lot4, "India",                 "Bangalore,Mumbai,New Delhi"),
            (Lot4, "Ireland",               "Dublin"),
            (Lot4, "Italy",                 "Milan,Rome"),
            (Lot4, "Japan",                 "Tokyo"),
            (Lot4, "Lebanon",               "Beirut"),
            (Lot4, "Malaysia",              "Kuala Lumpur"),
            (Lot4, "Mexico",                "Mexico City"),
            (Lot4, "Netherlands",           "Amsterdam"),
            (Lot4, "New Zealand",           "Wellington"),
            (Lot4, "Norway",                "Oslo"),
            (Lot4, "Philippines",           "Manila"),
            (Lot4, "Portugal",              "Amadora"),
            (Lot4, "Qatar",                 "Doha"),
            (Lot4, "Saudi Arabia",          "Riyadh"),
            (Lot4, "Singapore",             "Singapore"),
            (Lot4, "Slovakia",              "Bratislava"),
            (Lot4, "South Africa",          "Johannesburg"),
            (Lot4, "Spain",                 "Barcelona,Madrid"),
            (Lot4, "Sweden",                "Solna"),
            (Lot4, "Switzerland",           "Geneva,Zurich"),
            (Lot4, "Taiwan",                "Taipei"),
            (Lot4, "United Arab Emirates",  "Dubai"),
            (Lot4, "United Kingdom",        "Slough"),
            (Lot4, "United States",         "Atlanta,Chicago,Columbia,Dallas,Denver,Los Angeles,Manchester,Miami,Montgomery,New York,Oak Hill,Phoenix,Reston,San Francisco,St Paul,St Petersburg,Tulsa,Warren"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (lotName, country, cities) in SeedRows)
            {
                migrationBuilder.InsertData(
                    table: "LotCountryMappings",
                    columns: ["TenantId", "LotName", "Country", "Cities", "IsActive", "CreatedAt"],
                    values: new object[] { 1, lotName, country, cities, true, "2026-01-01 00:00:00" });
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var lotName in new[] { Lot1, Lot2, Lot4 })
            {
                migrationBuilder.Sql(
                    $"DELETE FROM LotCountryMappings WHERE TenantId = 1 AND LotName = '{lotName.Replace("'", "''")}';");
            }
        }
    }
}
