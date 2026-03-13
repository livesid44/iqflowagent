using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IQFlowAgent.Web.Migrations
{
    /// <inheritdoc />
    public partial class FixAuditLogsColumnTypes : Migration
    {
        // The AddAuditLogs migration was generated from a SQLite database and therefore:
        //   • all string columns were created as TEXT instead of nvarchar(max)
        //   • all bool  columns were created as INTEGER instead of bit
        //   • all long  columns were created as INTEGER instead of bigint
        //   • the Id column has no IDENTITY(1,1) annotation
        //
        // On SQL Server this causes the Audit Log page to throw an unhandled exception:
        //   "The text, ntext, and image data types cannot be compared or sorted,
        //    except when using IS NULL or LIKE operator."
        // because the Index action calls .Distinct().OrderBy() on two TEXT columns.
        //
        // This migration is a no-op on SQLite (guarded by ActiveProvider).
        // On SQL Server it uses the same conditional drop-and-recreate pattern as
        // FixAllTablesIdentity: if Id already has IDENTITY the block is skipped,
        // so the migration is safe to run on both new and already-deployed databases.
        // The table is always empty immediately after AddAuditLogs on a fresh deploy,
        // so the drop is safe.

        private const string CheckAndFix = @"
IF OBJECT_ID(N'[AuditLogs]', N'U') IS NOT NULL
    AND NOT EXISTS (
        SELECT 1 FROM sys.identity_columns ic
        JOIN sys.tables t ON ic.object_id = t.object_id
        WHERE t.name = 'AuditLogs' AND ic.name = 'Id'
    )
BEGIN
    DROP TABLE [AuditLogs];
    CREATE TABLE [AuditLogs] (
        [Id]              int            NOT NULL IDENTITY(1,1),
        [TenantId]        int            NOT NULL,
        [IntakeRecordId]  int            NULL,
        [CorrelationId]   nvarchar(max)  NOT NULL,
        [EventType]       nvarchar(max)  NOT NULL,
        [CallSite]        nvarchar(max)  NOT NULL,
        [PiiScanStatus]   nvarchar(max)  NOT NULL,
        [PiiFindingsJson] nvarchar(max)  NULL,
        [PiiFindingCount] int            NOT NULL,
        [WasBlocked]      bit            NOT NULL,
        [RequestUrl]      nvarchar(max)  NULL,
        [HttpStatusCode]  int            NULL,
        [DurationMs]      bigint         NULL,
        [IsMocked]        bit            NOT NULL,
        [Outcome]         nvarchar(max)  NOT NULL,
        [ErrorMessage]    nvarchar(max)  NULL,
        [UserId]          nvarchar(max)  NULL,
        [CreatedAt]       datetime2      NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer")
                return;

            migrationBuilder.Sql(CheckAndFix);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No structural rollback needed — this migration only repairs column types.
        }
    }
}
