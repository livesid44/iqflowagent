-- =============================================================================
-- IQFlow Agent — Combined RAG Pipeline SQL Migration Script
-- Applies ALL schema changes introduced by the RAG / Azure AI pipeline feature.
--
-- Covers:
--   1. 20260303122929_AddRagJobsAndSpeech
--   2. 20260327000000_AddRagSearchSettingsToTenantAiSettings
--
-- This script is IDEMPOTENT — safe to run multiple times on any environment.
-- Run this against your SQL Server database if EF Core migrations fail.
-- =============================================================================

SET NOCOUNT ON;
PRINT N'======================================================';
PRINT N' IQFlow Agent — RAG Pipeline Migration (Combined)';
PRINT N'======================================================';
PRINT N'';

-- ─────────────────────────────────────────────────────────────────────────────
-- PART 1 of 2: 20260303122929_AddRagJobsAndSpeech
-- Adds Azure Speech columns, IntakeDocuments transcript columns, RagJobs table.
-- ─────────────────────────────────────────────────────────────────────────────

PRINT N'--- Part 1: AddRagJobsAndSpeech ---';

-- 1a. TenantAiSettings: Azure Speech-to-Text settings

IF COL_LENGTH(N'TenantAiSettings', N'AzureSpeechApiKey') IS NULL
BEGIN
    ALTER TABLE [TenantAiSettings]
        ADD [AzureSpeechApiKey] nvarchar(max) NOT NULL DEFAULT N'';
    PRINT N'  + Added TenantAiSettings.AzureSpeechApiKey';
END
ELSE PRINT N'  = TenantAiSettings.AzureSpeechApiKey already exists';

IF COL_LENGTH(N'TenantAiSettings', N'AzureSpeechRegion') IS NULL
BEGIN
    ALTER TABLE [TenantAiSettings]
        ADD [AzureSpeechRegion] nvarchar(max) NOT NULL DEFAULT N'';
    PRINT N'  + Added TenantAiSettings.AzureSpeechRegion';
END
ELSE PRINT N'  = TenantAiSettings.AzureSpeechRegion already exists';

-- 1b. IntakeDocuments: transcript and generated SOP path columns

IF COL_LENGTH(N'IntakeDocuments', N'SopDocumentPath') IS NULL
BEGIN
    ALTER TABLE [IntakeDocuments]
        ADD [SopDocumentPath] nvarchar(max) NULL;
    PRINT N'  + Added IntakeDocuments.SopDocumentPath';
END
ELSE PRINT N'  = IntakeDocuments.SopDocumentPath already exists';

IF COL_LENGTH(N'IntakeDocuments', N'TranscriptStatus') IS NULL
BEGIN
    ALTER TABLE [IntakeDocuments]
        ADD [TranscriptStatus] nvarchar(max) NOT NULL DEFAULT N'';
    PRINT N'  + Added IntakeDocuments.TranscriptStatus';
END
ELSE PRINT N'  = IntakeDocuments.TranscriptStatus already exists';

IF COL_LENGTH(N'IntakeDocuments', N'TranscriptText') IS NULL
BEGIN
    ALTER TABLE [IntakeDocuments]
        ADD [TranscriptText] nvarchar(max) NULL;
    PRINT N'  + Added IntakeDocuments.TranscriptText';
END
ELSE PRINT N'  = IntakeDocuments.TranscriptText already exists';

-- 1c. RagJobs table (stores background RAG processing jobs per intake)

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
    PRINT N'  + Created table RagJobs';
END
ELSE PRINT N'  = Table RagJobs already exists';

-- 1d. Index on RagJobs.IntakeRecordId

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'RagJobs')
      AND [name] = N'IX_RagJobs_IntakeRecordId'
)
BEGIN
    CREATE INDEX [IX_RagJobs_IntakeRecordId]
        ON [RagJobs] ([IntakeRecordId]);
    PRINT N'  + Created index IX_RagJobs_IntakeRecordId';
END
ELSE PRINT N'  = Index IX_RagJobs_IntakeRecordId already exists';

-- 1e. EF migration history marker

IF NOT EXISTS (
    SELECT 1 FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260303122929_AddRagJobsAndSpeech'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260303122929_AddRagJobsAndSpeech', N'8.0.0');
    PRINT N'  + Recorded 20260303122929_AddRagJobsAndSpeech in __EFMigrationsHistory';
END
ELSE PRINT N'  = 20260303122929_AddRagJobsAndSpeech already in __EFMigrationsHistory';

PRINT N'';


-- ─────────────────────────────────────────────────────────────────────────────
-- PART 2 of 2: 20260327000000_AddRagSearchSettingsToTenantAiSettings
-- Adds Azure Document Intelligence, AI Search, and Embedding columns.
-- ─────────────────────────────────────────────────────────────────────────────

PRINT N'--- Part 2: AddRagSearchSettingsToTenantAiSettings ---';

-- 2a. Azure Document Intelligence

IF COL_LENGTH(N'TenantAiSettings', N'AzureDocumentIntelligenceEndpoint') IS NULL
BEGIN
    ALTER TABLE [TenantAiSettings]
        ADD [AzureDocumentIntelligenceEndpoint] nvarchar(max) NOT NULL DEFAULT N'';
    PRINT N'  + Added TenantAiSettings.AzureDocumentIntelligenceEndpoint';
END
ELSE PRINT N'  = TenantAiSettings.AzureDocumentIntelligenceEndpoint already exists';

IF COL_LENGTH(N'TenantAiSettings', N'AzureDocumentIntelligenceApiKey') IS NULL
BEGIN
    ALTER TABLE [TenantAiSettings]
        ADD [AzureDocumentIntelligenceApiKey] nvarchar(max) NOT NULL DEFAULT N'';
    PRINT N'  + Added TenantAiSettings.AzureDocumentIntelligenceApiKey';
END
ELSE PRINT N'  = TenantAiSettings.AzureDocumentIntelligenceApiKey already exists';

-- 2b. Embedding deployment name (defaults to text-embedding-3-small)

IF COL_LENGTH(N'TenantAiSettings', N'AzureOpenAIEmbeddingDeployment') IS NULL
BEGIN
    ALTER TABLE [TenantAiSettings]
        ADD [AzureOpenAIEmbeddingDeployment] nvarchar(max) NOT NULL DEFAULT N'text-embedding-3-small';
    PRINT N'  + Added TenantAiSettings.AzureOpenAIEmbeddingDeployment';
END
ELSE PRINT N'  = TenantAiSettings.AzureOpenAIEmbeddingDeployment already exists';

-- 2c. Azure AI Search

IF COL_LENGTH(N'TenantAiSettings', N'AzureSearchEndpoint') IS NULL
BEGIN
    ALTER TABLE [TenantAiSettings]
        ADD [AzureSearchEndpoint] nvarchar(max) NOT NULL DEFAULT N'';
    PRINT N'  + Added TenantAiSettings.AzureSearchEndpoint';
END
ELSE PRINT N'  = TenantAiSettings.AzureSearchEndpoint already exists';

IF COL_LENGTH(N'TenantAiSettings', N'AzureSearchApiKey') IS NULL
BEGIN
    ALTER TABLE [TenantAiSettings]
        ADD [AzureSearchApiKey] nvarchar(max) NOT NULL DEFAULT N'';
    PRINT N'  + Added TenantAiSettings.AzureSearchApiKey';
END
ELSE PRINT N'  = TenantAiSettings.AzureSearchApiKey already exists';

IF COL_LENGTH(N'TenantAiSettings', N'AzureSearchIndexName') IS NULL
BEGIN
    ALTER TABLE [TenantAiSettings]
        ADD [AzureSearchIndexName] nvarchar(max) NOT NULL DEFAULT N'iqflow-rag-chunks';
    PRINT N'  + Added TenantAiSettings.AzureSearchIndexName';
END
ELSE PRINT N'  = TenantAiSettings.AzureSearchIndexName already exists';

-- 2d. EF migration history marker

IF NOT EXISTS (
    SELECT 1 FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327000000_AddRagSearchSettingsToTenantAiSettings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260327000000_AddRagSearchSettingsToTenantAiSettings', N'8.0.0');
    PRINT N'  + Recorded 20260327000000_AddRagSearchSettingsToTenantAiSettings in __EFMigrationsHistory';
END
ELSE PRINT N'  = 20260327000000_AddRagSearchSettingsToTenantAiSettings already in __EFMigrationsHistory';

PRINT N'';
PRINT N'======================================================';
PRINT N' RAG Pipeline migration complete.';
PRINT N'======================================================';
