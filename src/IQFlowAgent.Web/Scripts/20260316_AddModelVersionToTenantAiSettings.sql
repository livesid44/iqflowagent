-- =============================================================================
-- IQFlowAgent – Incremental Migration Script (SQL Server)
-- Migration : 20260316063823_AddModelVersionToTenantAiSettings
-- =============================================================================
-- Purpose   : Adds the AzureOpenAIModelVersion column to TenantAiSettings so
--             administrators can choose between GPT-4o and GPT-5.2 per tenant.
--
-- What it does:
--   1. Adds column [AzureOpenAIModelVersion] nvarchar(max) NOT NULL DEFAULT 'gpt-5.2'
--      to the [TenantAiSettings] table.
--   2. Populates the column on all existing rows to 'gpt-5.2' (the recommended
--      default that gives the best response quality).
--   3. Records the migration in [__EFMigrationsHistory] so Entity Framework
--      does not try to re-apply it.
--
-- Safe to re-run: every step is guarded by a prior existence check.
--
-- Rollback   : A complete ROLLBACK block is provided at the bottom.
--
-- Usage (SSMS):
--   Run against the target database.  The script will print progress messages.
--
-- Usage (sqlcmd):
--   sqlcmd -S <server> -d <database> -i 20260316_AddModelVersionToTenantAiSettings.sql
--
-- Usage (Azure SQL / Azure Data Studio):
--   Open the script, connect to your database, and execute.
--
-- Affected table : TenantAiSettings
-- New column     : AzureOpenAIModelVersion  nvarchar(max)  NOT NULL  DEFAULT 'gpt-5.2'
-- =============================================================================

SET NOCOUNT ON;
GO

PRINT '=== IQFlowAgent Migration: 20260316063823_AddModelVersionToTenantAiSettings ===';
PRINT '';
GO

-- =============================================================================
-- Pre-flight check
-- =============================================================================
IF OBJECT_ID(N'[TenantAiSettings]', N'U') IS NULL
BEGIN
    PRINT '❌  ERROR: Table [TenantAiSettings] does not exist.';
    PRINT '    Please ensure the base schema is deployed before running this script.';
    RAISERROR('Table [TenantAiSettings] not found. Aborting.', 16, 1);
END
GO

-- =============================================================================
-- Check whether this migration has already been applied
-- =============================================================================
IF EXISTS (
    SELECT 1 FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316063823_AddModelVersionToTenantAiSettings'
)
BEGIN
    PRINT 'ℹ  Migration 20260316063823_AddModelVersionToTenantAiSettings is already recorded';
    PRINT '   in [__EFMigrationsHistory].  Skipping — nothing to do.';
    GOTO EndOfScript;
END
GO

-- =============================================================================
-- Migration: Add AzureOpenAIModelVersion column
-- =============================================================================
BEGIN TRANSACTION;

BEGIN TRY

    -- -----------------------------------------------------------------------
    -- Step 1: Add the column (idempotent — skipped if already present)
    -- -----------------------------------------------------------------------
    IF NOT EXISTS (
        SELECT 1
        FROM   sys.columns c
        JOIN   sys.tables  t ON c.object_id = t.object_id
        WHERE  t.name = N'TenantAiSettings'
          AND  c.name = N'AzureOpenAIModelVersion'
    )
    BEGIN
        ALTER TABLE [TenantAiSettings]
            ADD [AzureOpenAIModelVersion] nvarchar(max) NOT NULL
                CONSTRAINT [DF_TenantAiSettings_AzureOpenAIModelVersion] DEFAULT N'gpt-5.2';

        PRINT CONCAT('✔  Column [AzureOpenAIModelVersion] added to [TenantAiSettings]. ',
                     'Existing rows defaulted to ''gpt-5.2''.');
    END
    ELSE
    BEGIN
        PRINT 'ℹ  Column [AzureOpenAIModelVersion] already exists — skipping ALTER TABLE.';

        -- Ensure existing rows that might be empty are set to the default
        UPDATE [TenantAiSettings]
        SET    [AzureOpenAIModelVersion] = N'gpt-5.2'
        WHERE  [AzureOpenAIModelVersion] IS NULL
            OR [AzureOpenAIModelVersion] = N'';

        PRINT CONCAT('✔  Back-filled ', @@ROWCOUNT, ' row(s) that had no model version set.');
    END

    -- -----------------------------------------------------------------------
    -- Step 2: Record migration in __EFMigrationsHistory
    -- -----------------------------------------------------------------------
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260316063823_AddModelVersionToTenantAiSettings', N'8.0.13');

    PRINT '✔  Migration recorded in [__EFMigrationsHistory].';

    COMMIT TRANSACTION;
    PRINT '';
    PRINT '✅  Migration 20260316063823_AddModelVersionToTenantAiSettings COMMITTED successfully.';

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    PRINT '';
    PRINT '❌  ERROR — transaction rolled back. No changes were applied.';
    PRINT CONCAT('Error ',     ERROR_NUMBER(), ': ', ERROR_MESSAGE());
    PRINT CONCAT('Severity: ', ERROR_SEVERITY(), '  State: ', ERROR_STATE());
    PRINT CONCAT('Location: Line ', ERROR_LINE(),
                 ' in ', ISNULL(ERROR_PROCEDURE(), 'main script'));
    THROW;
END CATCH;
GO

-- =============================================================================
-- Post-migration verification
-- =============================================================================
PRINT '';
PRINT '=== Post-Migration Verification ===';

-- Confirm column exists
IF EXISTS (
    SELECT 1
    FROM   sys.columns c
    JOIN   sys.tables  t ON c.object_id = t.object_id
    WHERE  t.name = N'TenantAiSettings'
      AND  c.name = N'AzureOpenAIModelVersion'
)
    PRINT '✔  Column [AzureOpenAIModelVersion] confirmed present on [TenantAiSettings].';
ELSE
    PRINT '⚠  Column [AzureOpenAIModelVersion] NOT found — investigate.';

-- Show current values
SELECT
    [Id]                        AS TenantAiSettingsId,
    [TenantId],
    [AzureOpenAIDeploymentName],
    [AzureOpenAIModelVersion]
FROM [TenantAiSettings]
ORDER BY [Id];

-- Confirm migration history entry
IF EXISTS (
    SELECT 1 FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260316063823_AddModelVersionToTenantAiSettings'
)
    PRINT '✔  Migration entry confirmed in [__EFMigrationsHistory].';
ELSE
    PRINT '⚠  Migration entry NOT found in [__EFMigrationsHistory] — investigate.';

GO

-- =============================================================================
-- ROLLBACK INSTRUCTIONS
-- =============================================================================
-- To reverse this migration, run the block below in a separate session:
--
--   BEGIN TRANSACTION;
--   BEGIN TRY
--       -- Remove the column (drops the default constraint first)
--       DECLARE @constraintName nvarchar(256);
--       SELECT  @constraintName = dc.name
--       FROM    sys.default_constraints dc
--       JOIN    sys.columns             c  ON  dc.parent_object_id = c.object_id
--                                          AND dc.parent_column_id = c.column_id
--       JOIN    sys.tables              t  ON  c.object_id         = t.object_id
--       WHERE   t.name = N'TenantAiSettings'
--         AND   c.name = N'AzureOpenAIModelVersion';
--
--       IF @constraintName IS NOT NULL
--           EXEC('ALTER TABLE [TenantAiSettings] DROP CONSTRAINT [' + @constraintName + ']');
--
--       IF EXISTS (SELECT 1 FROM sys.columns c
--                  JOIN sys.tables t ON c.object_id = t.object_id
--                  WHERE t.name = 'TenantAiSettings' AND c.name = 'AzureOpenAIModelVersion')
--           ALTER TABLE [TenantAiSettings] DROP COLUMN [AzureOpenAIModelVersion];
--
--       -- Remove migration history entry
--       DELETE FROM [__EFMigrationsHistory]
--       WHERE  [MigrationId] = N'20260316063823_AddModelVersionToTenantAiSettings';
--
--       COMMIT TRANSACTION;
--       PRINT 'Rollback committed.';
--   END TRY
--   BEGIN CATCH
--       ROLLBACK TRANSACTION;
--       THROW;
--   END CATCH;
-- =============================================================================

EndOfScript:
PRINT '';
PRINT '=== Script complete ===';
GO
