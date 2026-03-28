-- =============================================================================
-- Migration: 20260303122929_AddRagJobsAndSpeech
-- Adds Azure Speech-to-Text columns to TenantAiSettings, transcript/SOP
-- columns to IntakeDocuments, and creates the RagJobs table.
--
-- This script is idempotent — safe to run multiple times.
-- Target: SQL Server 2019+
-- =============================================================================

-- ─── 1. TenantAiSettings: Azure Speech columns ───────────────────────────────

IF COL_LENGTH(N'TenantAiSettings', N'AzureSpeechApiKey') IS NULL
BEGIN
    ALTER TABLE [TenantAiSettings]
        ADD [AzureSpeechApiKey] nvarchar(max) NOT NULL DEFAULT N'';
    PRINT N'Added column TenantAiSettings.AzureSpeechApiKey';
END
ELSE
    PRINT N'Column TenantAiSettings.AzureSpeechApiKey already exists — skipped.';

IF COL_LENGTH(N'TenantAiSettings', N'AzureSpeechRegion') IS NULL
BEGIN
    ALTER TABLE [TenantAiSettings]
        ADD [AzureSpeechRegion] nvarchar(max) NOT NULL DEFAULT N'';
    PRINT N'Added column TenantAiSettings.AzureSpeechRegion';
END
ELSE
    PRINT N'Column TenantAiSettings.AzureSpeechRegion already exists — skipped.';

-- ─── 2. IntakeDocuments: transcript and SOP path columns ─────────────────────

IF COL_LENGTH(N'IntakeDocuments', N'SopDocumentPath') IS NULL
BEGIN
    ALTER TABLE [IntakeDocuments]
        ADD [SopDocumentPath] nvarchar(max) NULL;
    PRINT N'Added column IntakeDocuments.SopDocumentPath';
END
ELSE
    PRINT N'Column IntakeDocuments.SopDocumentPath already exists — skipped.';

IF COL_LENGTH(N'IntakeDocuments', N'TranscriptStatus') IS NULL
BEGIN
    ALTER TABLE [IntakeDocuments]
        ADD [TranscriptStatus] nvarchar(max) NOT NULL DEFAULT N'';
    PRINT N'Added column IntakeDocuments.TranscriptStatus';
END
ELSE
    PRINT N'Column IntakeDocuments.TranscriptStatus already exists — skipped.';

IF COL_LENGTH(N'IntakeDocuments', N'TranscriptText') IS NULL
BEGIN
    ALTER TABLE [IntakeDocuments]
        ADD [TranscriptText] nvarchar(max) NULL;
    PRINT N'Added column IntakeDocuments.TranscriptText';
END
ELSE
    PRINT N'Column IntakeDocuments.TranscriptText already exists — skipped.';

-- ─── 3. RagJobs table ────────────────────────────────────────────────────────

IF OBJECT_ID(N'RagJobs', N'U') IS NULL
BEGIN
    CREATE TABLE [RagJobs] (
        [Id]              int            NOT NULL IDENTITY(1,1),
        [IntakeRecordId]  int            NOT NULL,
        [Status]          nvarchar(max)  NOT NULL,
        [TotalFiles]      int            NOT NULL DEFAULT 0,
        [ProcessedFiles]  int            NOT NULL DEFAULT 0,
        [ErrorMessage]    nvarchar(max)  NULL,
        [CreatedAt]       datetime2      NOT NULL,
        [StartedAt]       datetime2      NULL,
        [CompletedAt]     datetime2      NULL,
        [NotifyUserId]    nvarchar(max)  NULL,

        CONSTRAINT [PK_RagJobs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RagJobs_IntakeRecords_IntakeRecordId]
            FOREIGN KEY ([IntakeRecordId])
            REFERENCES [IntakeRecords] ([Id])
            ON DELETE CASCADE
    );
    PRINT N'Created table RagJobs';
END
ELSE
    PRINT N'Table RagJobs already exists — skipped.';

-- ─── 4. Index on RagJobs.IntakeRecordId ──────────────────────────────────────

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'RagJobs')
      AND [name] = N'IX_RagJobs_IntakeRecordId'
)
BEGIN
    CREATE INDEX [IX_RagJobs_IntakeRecordId]
        ON [RagJobs] ([IntakeRecordId]);
    PRINT N'Created index IX_RagJobs_IntakeRecordId';
END
ELSE
    PRINT N'Index IX_RagJobs_IntakeRecordId already exists — skipped.';

-- ─── 5. EF __EFMigrationsHistory marker ──────────────────────────────────────
-- Insert the migration history record only if it is not already present.

IF NOT EXISTS (
    SELECT 1 FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260303122929_AddRagJobsAndSpeech'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260303122929_AddRagJobsAndSpeech', N'8.0.0');
    PRINT N'Recorded migration 20260303122929_AddRagJobsAndSpeech in __EFMigrationsHistory';
END
ELSE
    PRINT N'Migration 20260303122929_AddRagJobsAndSpeech already recorded — skipped.';

PRINT N'Migration 20260303122929_AddRagJobsAndSpeech complete.';
