using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IQFlowAgent.Web.Migrations
{
    /// <inheritdoc />
    public partial class FixLotCountryMappingsIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQL Server only: if the LotCountryMappings table exists but its Id column
            // was created without IDENTITY (e.g. from the original SQLite-scaffolded migration),
            // drop and recreate the table with IDENTITY(1,1) on Id.
            // This is a no-op when Id already has IDENTITY (new databases) or on SQLite.
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(@"
IF OBJECT_ID(N'[LotCountryMappings]', N'U') IS NOT NULL
    AND NOT EXISTS (
        SELECT 1
        FROM   sys.identity_columns ic
        JOIN   sys.tables           t  ON ic.object_id = t.object_id
        WHERE  t.name  = 'LotCountryMappings'
        AND    ic.name = 'Id'
    )
BEGIN
    DROP TABLE [LotCountryMappings];

    CREATE TABLE [LotCountryMappings] (
        [Id]        int             NOT NULL IDENTITY(1,1),
        [TenantId]  int             NOT NULL,
        [LotName]   nvarchar(max)   NOT NULL,
        [Country]   nvarchar(max)   NOT NULL,
        [Cities]    nvarchar(max)   NOT NULL,
        [IsActive]  bit             NOT NULL,
        [CreatedAt] datetime2       NOT NULL,
        CONSTRAINT [PK_LotCountryMappings] PRIMARY KEY ([Id])
    );
END
");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the table definition is managed by AddLotCountryMapping.
            // Rolling back to the broken (no-IDENTITY) state is intentionally not supported.
        }
    }
}
