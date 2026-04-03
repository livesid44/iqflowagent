-- =============================================================================
-- Migration: 20260329000000_AddBartokSectionNameToIntakeTask
-- Adds BartokSectionName column to IntakeTasks for checkpoint task targeting.
--
-- This script is idempotent — safe to run multiple times.
-- Target: SQL Server 2019+
-- =============================================================================

IF COL_LENGTH(N'IntakeTasks', N'BartokSectionName') IS NULL
BEGIN
    ALTER TABLE [IntakeTasks]
        ADD [BartokSectionName] nvarchar(max) NULL;
    PRINT N'Added column IntakeTasks.BartokSectionName';
END
ELSE
    PRINT N'Column IntakeTasks.BartokSectionName already exists — skipped.';

-- Record the migration in EF history (only if not already present)
IF NOT EXISTS (
    SELECT 1 FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260329000000_AddBartokSectionNameToIntakeTask'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260329000000_AddBartokSectionNameToIntakeTask', N'8.0.13');
    PRINT N'Migration recorded in __EFMigrationsHistory.';
END
ELSE
    PRINT N'Migration already recorded in __EFMigrationsHistory — skipped.';
