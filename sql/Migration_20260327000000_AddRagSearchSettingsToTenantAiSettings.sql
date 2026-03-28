-- =============================================================================
-- Migration: 20260327000000_AddRagSearchSettingsToTenantAiSettings
-- Adds Azure Document Intelligence, Azure AI Search, and Embedding
-- configuration columns to TenantAiSettings.
--
-- This script is idempotent — safe to run multiple times.
-- Target: SQL Server 2019+
-- =============================================================================

-- ─── 1. Document Intelligence settings ───────────────────────────────────────

IF COL_LENGTH(N'TenantAiSettings', N'AzureDocumentIntelligenceEndpoint') IS NULL
BEGIN
    ALTER TABLE [TenantAiSettings]
        ADD [AzureDocumentIntelligenceEndpoint] nvarchar(max) NOT NULL DEFAULT N'';
    PRINT N'Added column TenantAiSettings.AzureDocumentIntelligenceEndpoint';
END
ELSE
    PRINT N'Column TenantAiSettings.AzureDocumentIntelligenceEndpoint already exists — skipped.';

IF COL_LENGTH(N'TenantAiSettings', N'AzureDocumentIntelligenceApiKey') IS NULL
BEGIN
    ALTER TABLE [TenantAiSettings]
        ADD [AzureDocumentIntelligenceApiKey] nvarchar(max) NOT NULL DEFAULT N'';
    PRINT N'Added column TenantAiSettings.AzureDocumentIntelligenceApiKey';
END
ELSE
    PRINT N'Column TenantAiSettings.AzureDocumentIntelligenceApiKey already exists — skipped.';

-- ─── 2. Embedding deployment name ────────────────────────────────────────────

IF COL_LENGTH(N'TenantAiSettings', N'AzureOpenAIEmbeddingDeployment') IS NULL
BEGIN
    ALTER TABLE [TenantAiSettings]
        ADD [AzureOpenAIEmbeddingDeployment] nvarchar(max) NOT NULL DEFAULT N'text-embedding-3-small';
    PRINT N'Added column TenantAiSettings.AzureOpenAIEmbeddingDeployment';
END
ELSE
    PRINT N'Column TenantAiSettings.AzureOpenAIEmbeddingDeployment already exists — skipped.';

-- ─── 3. Azure AI Search settings ─────────────────────────────────────────────

IF COL_LENGTH(N'TenantAiSettings', N'AzureSearchEndpoint') IS NULL
BEGIN
    ALTER TABLE [TenantAiSettings]
        ADD [AzureSearchEndpoint] nvarchar(max) NOT NULL DEFAULT N'';
    PRINT N'Added column TenantAiSettings.AzureSearchEndpoint';
END
ELSE
    PRINT N'Column TenantAiSettings.AzureSearchEndpoint already exists — skipped.';

IF COL_LENGTH(N'TenantAiSettings', N'AzureSearchApiKey') IS NULL
BEGIN
    ALTER TABLE [TenantAiSettings]
        ADD [AzureSearchApiKey] nvarchar(max) NOT NULL DEFAULT N'';
    PRINT N'Added column TenantAiSettings.AzureSearchApiKey';
END
ELSE
    PRINT N'Column TenantAiSettings.AzureSearchApiKey already exists — skipped.';

IF COL_LENGTH(N'TenantAiSettings', N'AzureSearchIndexName') IS NULL
BEGIN
    ALTER TABLE [TenantAiSettings]
        ADD [AzureSearchIndexName] nvarchar(max) NOT NULL DEFAULT N'iqflow-rag-chunks';
    PRINT N'Added column TenantAiSettings.AzureSearchIndexName';
END
ELSE
    PRINT N'Column TenantAiSettings.AzureSearchIndexName already exists — skipped.';

-- ─── 4. EF __EFMigrationsHistory marker ──────────────────────────────────────

IF NOT EXISTS (
    SELECT 1 FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260327000000_AddRagSearchSettingsToTenantAiSettings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260327000000_AddRagSearchSettingsToTenantAiSettings', N'8.0.0');
    PRINT N'Recorded migration 20260327000000_AddRagSearchSettingsToTenantAiSettings in __EFMigrationsHistory';
END
ELSE
    PRINT N'Migration 20260327000000_AddRagSearchSettingsToTenantAiSettings already recorded — skipped.';

PRINT N'Migration 20260327000000_AddRagSearchSettingsToTenantAiSettings complete.';
