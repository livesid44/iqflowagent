-- =============================================================================
-- IQFlowAgent – Full Database Setup Script (SQL Server)
-- =============================================================================
-- Run this script against an empty database OR an existing one.
-- Every block is idempotent: it checks __EFMigrationsHistory before applying,
-- so the script is safe to re-run at any time.
--
-- Usage (SSMS / sqlcmd):
--   sqlcmd -S <server> -d <database> -i database-migrate.sql
--
-- This script is the authoritative source for the database schema.
-- It covers all migrations up to: 20260316063823_AddModelVersionToTenantAiSettings
-- =============================================================================

IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId]    nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32)  NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

-- =============================================================================
-- 20260222115540_InitialCreate
-- =============================================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260222115540_InitialCreate')
BEGIN
    CREATE TABLE [AspNetRoles] (
        [Id]               nvarchar(450) NOT NULL,
        [Name]             nvarchar(256) NULL,
        [NormalizedName]   nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260222115540_InitialCreate')
BEGIN
    CREATE TABLE [AspNetUsers] (
        [Id]                   nvarchar(450) NOT NULL,
        [FullName]             nvarchar(max) NOT NULL,
        [IsActive]             bit           NOT NULL,
        [LastLogin]            datetime2     NULL,
        [CreatedAt]            datetime2     NOT NULL,
        [UserName]             nvarchar(256) NULL,
        [NormalizedUserName]   nvarchar(256) NULL,
        [Email]                nvarchar(256) NULL,
        [NormalizedEmail]      nvarchar(256) NULL,
        [EmailConfirmed]       bit           NOT NULL,
        [PasswordHash]         nvarchar(max) NULL,
        [SecurityStamp]        nvarchar(max) NULL,
        [ConcurrencyStamp]     nvarchar(max) NULL,
        [PhoneNumber]          nvarchar(max) NULL,
        [PhoneNumberConfirmed] bit           NOT NULL,
        [TwoFactorEnabled]     bit           NOT NULL,
        [LockoutEnd]           datetimeoffset NULL,
        [LockoutEnabled]       bit           NOT NULL,
        [AccessFailedCount]    int           NOT NULL,
        CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260222115540_InitialCreate')
BEGIN
    CREATE TABLE [AuthSettings] (
        [Id]               int           NOT NULL IDENTITY,
        [AuthMode]         nvarchar(max) NOT NULL,
        [LdapServer]       nvarchar(max) NULL,
        [LdapPort]         int           NOT NULL,
        [LdapBaseDn]       nvarchar(max) NULL,
        [LdapBindDn]       nvarchar(max) NULL,
        [LdapBindPassword] nvarchar(max) NULL,
        [LdapUseSsl]       bit           NOT NULL,
        [LdapSearchFilter] nvarchar(max) NULL,
        [UpdatedAt]        datetime2     NOT NULL,
        CONSTRAINT [PK_AuthSettings] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260222115540_InitialCreate')
BEGIN
    CREATE TABLE [IntakeRecords] (
        [Id]                      int           NOT NULL IDENTITY,
        [IntakeId]                nvarchar(max) NOT NULL,
        [ProcessName]             nvarchar(max) NOT NULL,
        [Description]             nvarchar(max) NOT NULL,
        [BusinessUnit]            nvarchar(max) NOT NULL,
        [Department]              nvarchar(max) NOT NULL,
        [ProcessOwnerName]        nvarchar(max) NOT NULL,
        [ProcessOwnerEmail]       nvarchar(max) NOT NULL,
        [ProcessType]             nvarchar(max) NOT NULL,
        [EstimatedVolumePerDay]   int           NOT NULL,
        [Priority]                nvarchar(max) NOT NULL,
        [Country]                 nvarchar(max) NOT NULL,
        [City]                    nvarchar(max) NOT NULL,
        [SiteLocation]            nvarchar(max) NOT NULL,
        [TimeZone]                nvarchar(max) NOT NULL,
        [UploadedFileName]        nvarchar(max) NULL,
        [UploadedFilePath]        nvarchar(max) NULL,
        [UploadedFileContentType] nvarchar(max) NULL,
        [UploadedFileSize]        bigint        NULL,
        [Status]                  nvarchar(max) NOT NULL,
        [AnalysisResult]          nvarchar(max) NULL,
        [CreatedAt]               datetime2     NOT NULL,
        [SubmittedAt]             datetime2     NULL,
        [AnalyzedAt]              datetime2     NULL,
        [CreatedByUserId]         nvarchar(max) NULL,
        CONSTRAINT [PK_IntakeRecords] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260222115540_InitialCreate')
BEGIN
    CREATE TABLE [AspNetRoleClaims] (
        [Id]         int           NOT NULL IDENTITY,
        [RoleId]     nvarchar(450) NOT NULL,
        [ClaimType]  nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId]
            FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260222115540_InitialCreate')
BEGIN
    CREATE TABLE [AspNetUserClaims] (
        [Id]         int           NOT NULL IDENTITY,
        [UserId]     nvarchar(450) NOT NULL,
        [ClaimType]  nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId]
            FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260222115540_InitialCreate')
BEGIN
    CREATE TABLE [AspNetUserLogins] (
        [LoginProvider]       nvarchar(128) NOT NULL,
        [ProviderKey]         nvarchar(128) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId]              nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId]
            FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260222115540_InitialCreate')
BEGIN
    CREATE TABLE [AspNetUserRoles] (
        [UserId] nvarchar(450) NOT NULL,
        [RoleId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId]
            FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId]
            FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260222115540_InitialCreate')
BEGIN
    CREATE TABLE [AspNetUserTokens] (
        [UserId]        nvarchar(450) NOT NULL,
        [LoginProvider] nvarchar(128) NOT NULL,
        [Name]          nvarchar(128) NOT NULL,
        [Value]         nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId]
            FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260222115540_InitialCreate')
BEGIN
    CREATE TABLE [FinalReports] (
        [Id]                int           NOT NULL IDENTITY,
        [IntakeRecordId]    int           NOT NULL,
        [ReportFileName]    nvarchar(max) NOT NULL,
        [FilePath]          nvarchar(max) NOT NULL,
        [FileSizeBytes]     bigint        NOT NULL,
        [GeneratedAt]       datetime2     NOT NULL,
        [GeneratedByUserId] nvarchar(max) NULL,
        [GeneratedByName]   nvarchar(max) NULL,
        CONSTRAINT [PK_FinalReports] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FinalReports_IntakeRecords_IntakeRecordId]
            FOREIGN KEY ([IntakeRecordId]) REFERENCES [IntakeRecords] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260222115540_InitialCreate')
BEGIN
    CREATE TABLE [IntakeTasks] (
        [Id]              int           NOT NULL IDENTITY,
        [TaskId]          nvarchar(max) NOT NULL,
        [IntakeRecordId]  int           NOT NULL,
        [Title]           nvarchar(max) NOT NULL,
        [Description]     nvarchar(max) NOT NULL,
        [Owner]           nvarchar(max) NOT NULL,
        [Priority]        nvarchar(max) NOT NULL,
        [Status]          nvarchar(max) NOT NULL,
        [CreatedAt]       datetime2     NOT NULL,
        [DueDate]         datetime2     NOT NULL,
        [CompletedAt]     datetime2     NULL,
        [CreatedByUserId] nvarchar(max) NULL,
        CONSTRAINT [PK_IntakeTasks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_IntakeTasks_IntakeRecords_IntakeRecordId]
            FOREIGN KEY ([IntakeRecordId]) REFERENCES [IntakeRecords] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260222115540_InitialCreate')
BEGIN
    CREATE TABLE [ReportFieldStatuses] (
        [Id]                  int           NOT NULL IDENTITY,
        [IntakeRecordId]      int           NOT NULL,
        [FieldKey]            nvarchar(max) NOT NULL,
        [FieldLabel]          nvarchar(max) NOT NULL,
        [Section]             nvarchar(max) NOT NULL,
        [TemplatePlaceholder] nvarchar(max) NOT NULL,
        [Status]              nvarchar(max) NOT NULL,
        [FillValue]           nvarchar(max) NULL,
        [IsNA]                bit           NOT NULL,
        [Notes]               nvarchar(max) NULL,
        [LinkedTaskId]        nvarchar(max) NULL,
        [AnalyzedAt]          datetime2     NULL,
        [UpdatedAt]           datetime2     NOT NULL,
        CONSTRAINT [PK_ReportFieldStatuses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ReportFieldStatuses_IntakeRecords_IntakeRecordId]
            FOREIGN KEY ([IntakeRecordId]) REFERENCES [IntakeRecords] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260222115540_InitialCreate')
BEGIN
    CREATE TABLE [IntakeDocuments] (
        [Id]              int           NOT NULL IDENTITY,
        [IntakeRecordId]  int           NOT NULL,
        [IntakeTaskId]    int           NULL,
        [FileName]        nvarchar(max) NOT NULL,
        [FilePath]        nvarchar(max) NOT NULL,
        [ContentType]     nvarchar(max) NULL,
        [FileSize]        bigint        NULL,
        [DocumentType]    nvarchar(max) NOT NULL,
        [UploadedAt]      datetime2     NOT NULL,
        [UploadedByUserId] nvarchar(max) NULL,
        [UploadedByName]  nvarchar(max) NULL,
        CONSTRAINT [PK_IntakeDocuments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_IntakeDocuments_IntakeRecords_IntakeRecordId]
            FOREIGN KEY ([IntakeRecordId]) REFERENCES [IntakeRecords] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_IntakeDocuments_IntakeTasks_IntakeTaskId]
            FOREIGN KEY ([IntakeTaskId]) REFERENCES [IntakeTasks] ([Id])
    );
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260222115540_InitialCreate')
BEGIN
    CREATE TABLE [TaskActionLogs] (
        [Id]              int           NOT NULL IDENTITY,
        [IntakeTaskId]    int           NOT NULL,
        [ActionType]      nvarchar(max) NOT NULL,
        [OldStatus]       nvarchar(max) NULL,
        [NewStatus]       nvarchar(max) NULL,
        [Comment]         nvarchar(max) NULL,
        [CreatedAt]       datetime2     NOT NULL,
        [CreatedByUserId] nvarchar(max) NULL,
        [CreatedByName]   nvarchar(max) NULL,
        CONSTRAINT [PK_TaskActionLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TaskActionLogs_IntakeTasks_IntakeTaskId]
            FOREIGN KEY ([IntakeTaskId]) REFERENCES [IntakeTasks] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260222115540_InitialCreate')
BEGIN
    CREATE INDEX [IX_AspNetRoleClaims_RoleId]       ON [AspNetRoleClaims]    ([RoleId]);
    CREATE UNIQUE INDEX [RoleNameIndex]              ON [AspNetRoles]         ([NormalizedName]);
    CREATE INDEX [IX_AspNetUserClaims_UserId]        ON [AspNetUserClaims]    ([UserId]);
    CREATE INDEX [IX_AspNetUserLogins_UserId]        ON [AspNetUserLogins]    ([UserId]);
    CREATE INDEX [IX_AspNetUserRoles_RoleId]         ON [AspNetUserRoles]     ([RoleId]);
    CREATE INDEX [EmailIndex]                        ON [AspNetUsers]         ([NormalizedEmail]);
    CREATE UNIQUE INDEX [UserNameIndex]              ON [AspNetUsers]         ([NormalizedUserName]);
    CREATE INDEX [IX_FinalReports_IntakeRecordId]    ON [FinalReports]        ([IntakeRecordId]);
    CREATE INDEX [IX_IntakeDocuments_IntakeRecordId] ON [IntakeDocuments]     ([IntakeRecordId]);
    CREATE INDEX [IX_IntakeDocuments_IntakeTaskId]   ON [IntakeDocuments]     ([IntakeTaskId]);
    CREATE INDEX [IX_IntakeTasks_IntakeRecordId]     ON [IntakeTasks]         ([IntakeRecordId]);
    CREATE INDEX [IX_ReportFieldStatuses_IntakeRecordId] ON [ReportFieldStatuses] ([IntakeRecordId]);
    CREATE INDEX [IX_TaskActionLogs_IntakeTaskId]    ON [TaskActionLogs]      ([IntakeTaskId]);
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260222115540_InitialCreate')
    INSERT INTO [__EFMigrationsHistory] VALUES (N'20260222115540_InitialCreate', N'8.0.13');
GO

COMMIT;
GO

-- =============================================================================
-- 20260224070035_UpdateModels  (no schema changes)
-- =============================================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260224070035_UpdateModels')
    INSERT INTO [__EFMigrationsHistory] VALUES (N'20260224070035_UpdateModels', N'8.0.13');
GO

COMMIT;
GO

-- =============================================================================
-- 20260224134547_AddTaskNotApplicable
-- =============================================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260224134547_AddTaskNotApplicable')
BEGIN
    ALTER TABLE [IntakeTasks] ADD [IsNotApplicable] bit           NOT NULL DEFAULT CAST(0 AS bit);
    ALTER TABLE [IntakeTasks] ADD [NaReason]        nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260224134547_AddTaskNotApplicable')
    INSERT INTO [__EFMigrationsHistory] VALUES (N'20260224134547_AddTaskNotApplicable', N'8.0.13');
GO

COMMIT;
GO

-- =============================================================================
-- 20260302114249_AddMasterDepartmentAndQcCheck
-- =============================================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260302114249_AddMasterDepartmentAndQcCheck')
BEGIN
    CREATE TABLE [MasterDepartments] (
        [Id]          int           NOT NULL IDENTITY,
        [Name]        nvarchar(max) NOT NULL,
        [Description] nvarchar(max) NULL,
        [IsActive]    bit           NOT NULL,
        [CreatedAt]   datetime2     NOT NULL,
        CONSTRAINT [PK_MasterDepartments] PRIMARY KEY ([Id])
    );

    CREATE TABLE [QcChecks] (
        [Id]                 int           NOT NULL IDENTITY,
        [IntakeRecordId]     int           NOT NULL,
        [OverallScore]       int           NOT NULL,
        [ScoreBreakdownJson] nvarchar(max) NULL,
        [Status]             nvarchar(max) NOT NULL,
        [ErrorMessage]       nvarchar(max) NULL,
        [CreatedAt]          datetime2     NOT NULL,
        [CompletedAt]        datetime2     NULL,
        [RunByUserId]        nvarchar(max) NULL,
        CONSTRAINT [PK_QcChecks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_QcChecks_IntakeRecords_IntakeRecordId]
            FOREIGN KEY ([IntakeRecordId]) REFERENCES [IntakeRecords] ([Id]) ON DELETE CASCADE
    );

    CREATE INDEX [IX_QcChecks_IntakeRecordId] ON [QcChecks] ([IntakeRecordId]);
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260302114249_AddMasterDepartmentAndQcCheck')
    INSERT INTO [__EFMigrationsHistory] VALUES (N'20260302114249_AddMasterDepartmentAndQcCheck', N'8.0.13');
GO

COMMIT;
GO

-- =============================================================================
-- 20260303075808_AddMultiTenant
-- =============================================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260303075808_AddMultiTenant')
BEGIN
    ALTER TABLE [QcChecks]       ADD [TenantId] int NOT NULL DEFAULT 1;
    ALTER TABLE [MasterDepartments] ADD [TenantId] int NOT NULL DEFAULT 1;
    ALTER TABLE [IntakeRecords]   ADD [TenantId] int NOT NULL DEFAULT 1;
    ALTER TABLE [AuthSettings]    ADD [TenantId] int NOT NULL DEFAULT 1;

    CREATE TABLE [Tenants] (
        [Id]          int           NOT NULL IDENTITY,
        [Name]        nvarchar(max) NOT NULL,
        [Slug]        nvarchar(max) NOT NULL,
        [Color]       nvarchar(max) NOT NULL,
        [Description] nvarchar(max) NULL,
        [IsActive]    bit           NOT NULL,
        [CreatedAt]   datetime2     NOT NULL,
        CONSTRAINT [PK_Tenants] PRIMARY KEY ([Id])
    );

    CREATE TABLE [TenantAiSettings] (
        [Id]                              int           NOT NULL IDENTITY,
        [TenantId]                        int           NOT NULL,
        [AzureOpenAIEndpoint]             nvarchar(max) NOT NULL,
        [AzureOpenAIApiKey]               nvarchar(max) NOT NULL,
        [AzureOpenAIDeploymentName]       nvarchar(max) NOT NULL,
        [AzureOpenAIApiVersion]           nvarchar(max) NOT NULL,
        [AzureOpenAIMaxTokens]            int           NOT NULL,
        [AzureStorageConnectionString]    nvarchar(max) NOT NULL,
        [AzureStorageContainerName]       nvarchar(max) NOT NULL,
        [UpdatedAt]                       datetime2     NOT NULL,
        [UpdatedByUserId]                 nvarchar(max) NULL,
        CONSTRAINT [PK_TenantAiSettings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TenantAiSettings_Tenants_TenantId]
            FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE CASCADE
    );

    CREATE TABLE [UserTenants] (
        [Id]         int           NOT NULL IDENTITY,
        [UserId]     nvarchar(450) NOT NULL,
        [TenantId]   int           NOT NULL,
        [TenantRole] nvarchar(max) NOT NULL,
        [IsDefault]  bit           NOT NULL,
        [AssignedAt] datetime2     NOT NULL,
        CONSTRAINT [PK_UserTenants] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserTenants_AspNetUsers_UserId]
            FOREIGN KEY ([UserId])   REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_UserTenants_Tenants_TenantId]
            FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );

    CREATE INDEX [IX_TenantAiSettings_TenantId] ON [TenantAiSettings] ([TenantId]);
    CREATE INDEX [IX_UserTenants_TenantId]       ON [UserTenants]      ([TenantId]);
    CREATE INDEX [IX_UserTenants_UserId]         ON [UserTenants]      ([UserId]);
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260303075808_AddMultiTenant')
    INSERT INTO [__EFMigrationsHistory] VALUES (N'20260303075808_AddMultiTenant', N'8.0.13');
GO

COMMIT;
GO

-- =============================================================================
-- 20260303115405_AddLobAndCountryMultiSelect
-- =============================================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260303115405_AddLobAndCountryMultiSelect')
BEGIN
    ALTER TABLE [IntakeRecords] ADD [Lob] nvarchar(max) NOT NULL DEFAULT N'';

    CREATE TABLE [MasterLobs] (
        [Id]             int           NOT NULL IDENTITY,
        [TenantId]       int           NOT NULL,
        [DepartmentName] nvarchar(max) NOT NULL,
        [Name]           nvarchar(max) NOT NULL,
        [Description]    nvarchar(max) NULL,
        [IsActive]       bit           NOT NULL,
        [CreatedAt]      datetime2     NOT NULL,
        CONSTRAINT [PK_MasterLobs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260303115405_AddLobAndCountryMultiSelect')
    INSERT INTO [__EFMigrationsHistory] VALUES (N'20260303115405_AddLobAndCountryMultiSelect', N'8.0.13');
GO

COMMIT;
GO

-- =============================================================================
-- 20260303122929_AddRagJobsAndSpeech
-- =============================================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260303122929_AddRagJobsAndSpeech')
BEGIN
    ALTER TABLE [TenantAiSettings]  ADD [AzureSpeechApiKey]  nvarchar(max) NOT NULL DEFAULT N'';
    ALTER TABLE [TenantAiSettings]  ADD [AzureSpeechRegion]  nvarchar(max) NOT NULL DEFAULT N'';
    ALTER TABLE [IntakeDocuments]   ADD [SopDocumentPath]    nvarchar(max) NULL;
    ALTER TABLE [IntakeDocuments]   ADD [TranscriptStatus]   nvarchar(max) NOT NULL DEFAULT N'';
    ALTER TABLE [IntakeDocuments]   ADD [TranscriptText]     nvarchar(max) NULL;

    CREATE TABLE [RagJobs] (
        [Id]             int           NOT NULL IDENTITY,
        [IntakeRecordId] int           NOT NULL,
        [Status]         nvarchar(max) NOT NULL,
        [TotalFiles]     int           NOT NULL,
        [ProcessedFiles] int           NOT NULL,
        [ErrorMessage]   nvarchar(max) NULL,
        [CreatedAt]      datetime2     NOT NULL,
        [StartedAt]      datetime2     NULL,
        [CompletedAt]    datetime2     NULL,
        [NotifyUserId]   nvarchar(max) NULL,
        CONSTRAINT [PK_RagJobs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RagJobs_IntakeRecords_IntakeRecordId]
            FOREIGN KEY ([IntakeRecordId]) REFERENCES [IntakeRecords] ([Id]) ON DELETE CASCADE
    );

    CREATE INDEX [IX_RagJobs_IntakeRecordId] ON [RagJobs] ([IntakeRecordId]);
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260303122929_AddRagJobsAndSpeech')
    INSERT INTO [__EFMigrationsHistory] VALUES (N'20260303122929_AddRagJobsAndSpeech', N'8.0.13');
GO

COMMIT;
GO

-- =============================================================================
-- 20260305055344_AddUniqueIndexToTenantAiSettings
-- =============================================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260305055344_AddUniqueIndexToTenantAiSettings')
BEGIN
    DROP INDEX [IX_TenantAiSettings_TenantId] ON [TenantAiSettings];
    CREATE UNIQUE INDEX [IX_TenantAiSettings_TenantId] ON [TenantAiSettings] ([TenantId]);
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260305055344_AddUniqueIndexToTenantAiSettings')
    INSERT INTO [__EFMigrationsHistory] VALUES (N'20260305055344_AddUniqueIndexToTenantAiSettings', N'8.0.13');
GO

COMMIT;
GO

-- =============================================================================
-- 20260309075501_AddSdcLots
-- =============================================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260309075501_AddSdcLots')
BEGIN
    ALTER TABLE [IntakeRecords] ADD [SdcLots] nvarchar(max) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260309075501_AddSdcLots')
    INSERT INTO [__EFMigrationsHistory] VALUES (N'20260309075501_AddSdcLots', N'8.0.13');
GO

COMMIT;
GO

-- =============================================================================
-- 20260309092228_AddLotCountryMapping
-- Creates LotCountryMappings with IDENTITY on Id (correct for SQL Server).
-- =============================================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260309092228_AddLotCountryMapping')
BEGIN
    ALTER TABLE [TenantAiSettings] ADD [UseCountryFilterByLot] bit NOT NULL DEFAULT CAST(0 AS bit);

    CREATE TABLE [LotCountryMappings] (
        [Id]        int           NOT NULL IDENTITY,
        [TenantId]  int           NOT NULL,
        [LotName]   nvarchar(max) NOT NULL,
        [Country]   nvarchar(max) NOT NULL,
        [Cities]    nvarchar(max) NOT NULL,
        [IsActive]  bit           NOT NULL,
        [CreatedAt] datetime2     NOT NULL,
        CONSTRAINT [PK_LotCountryMappings] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260309092228_AddLotCountryMapping')
    INSERT INTO [__EFMigrationsHistory] VALUES (N'20260309092228_AddLotCountryMapping', N'8.0.13');
GO

COMMIT;
GO

-- =============================================================================
-- 20260309095000_FixLotCountryMappingsIdentity
-- Repairs databases where AddLotCountryMapping ran WITHOUT IDENTITY on Id.
-- Safe no-op when IDENTITY already exists (new databases or already fixed ones).
-- =============================================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260309095000_FixLotCountryMappingsIdentity')
BEGIN
    IF OBJECT_ID(N'[LotCountryMappings]', N'U') IS NOT NULL
        AND NOT EXISTS (
            SELECT 1
            FROM   sys.identity_columns ic
            JOIN   sys.tables           t  ON ic.object_id = t.object_id
            WHERE  t.name  = 'LotCountryMappings'
            AND    ic.name = 'Id'
        )
    BEGIN
        -- Table exists but Id has no IDENTITY — drop the empty table and recreate correctly.
        DROP TABLE [LotCountryMappings];

        CREATE TABLE [LotCountryMappings] (
            [Id]        int           NOT NULL IDENTITY(1,1),
            [TenantId]  int           NOT NULL,
            [LotName]   nvarchar(max) NOT NULL,
            [Country]   nvarchar(max) NOT NULL,
            [Cities]    nvarchar(max) NOT NULL,
            [IsActive]  bit           NOT NULL,
            [CreatedAt] datetime2     NOT NULL,
            CONSTRAINT [PK_LotCountryMappings] PRIMARY KEY ([Id])
        );
    END
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260309095000_FixLotCountryMappingsIdentity')
    INSERT INTO [__EFMigrationsHistory] VALUES (N'20260309095000_FixLotCountryMappingsIdentity', N'8.0.13');
GO

COMMIT;
GO

-- =============================================================================
-- 20260309101022_SeedLotCountryMappings
-- =============================================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260309101022_SeedLotCountryMappings')
BEGIN
    -- Lot 1 – Global Customer Support
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 1 – Global Customer Support',N'Brazil',        N'Petropolis',                                                                                                               1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 1 – Global Customer Support',N'China',         N'Beijing',                                                                                                                  1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 1 – Global Customer Support',N'Egypt',         N'Cairo',                                                                                                                    1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 1 – Global Customer Support',N'Greece',        N'Kallithea Attica',                                                                                                         1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 1 – Global Customer Support',N'India',         N'Mumbai,New Delhi',                                                                                                         1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 1 – Global Customer Support',N'Italy',         N'Milan,Rome',                                                                                                               1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 1 – Global Customer Support',N'Malaysia',      N'Kuala Lumpur',                                                                                                             1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 1 – Global Customer Support',N'Romania',       N'Bucharest',                                                                                                                1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 1 – Global Customer Support',N'Turkey',        N'Istanbul',                                                                                                                 1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 1 – Global Customer Support',N'United States', N'Atlanta,Phoenix',                                                                                                          1,'2026-01-01');

    -- Lot 2 – Quote to Bill
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 2 – Quote to Bill',N'Brazil',                N'Petropolis',                                                                                                               1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 2 – Quote to Bill',N'Chile',                 N'Santiago de Chile',                                                                                                        1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 2 – Quote to Bill',N'Czech Republic',        N'Prague',                                                                                                                   1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 2 – Quote to Bill',N'Egypt',                 N'Cairo',                                                                                                                    1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 2 – Quote to Bill',N'Germany',               N'Frankfurt',                                                                                                                1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 2 – Quote to Bill',N'Greece',                N'Kallithea Attica',                                                                                                         1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 2 – Quote to Bill',N'Hong Kong',             N'Quarry Bay',                                                                                                               1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 2 – Quote to Bill',N'India',                 N'Bangalore,Hyderabad,Mumbai,New Delhi',                                                                                     1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 2 – Quote to Bill',N'Italy',                 N'Milan,Rome',                                                                                                               1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 2 – Quote to Bill',N'Lebanon',               N'Beirut',                                                                                                                   1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 2 – Quote to Bill',N'Malaysia',              N'Kuala Lumpur',                                                                                                             1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 2 – Quote to Bill',N'Netherlands',           N'Amsterdam',                                                                                                                1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 2 – Quote to Bill',N'Portugal',              N'Amadora',                                                                                                                  1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 2 – Quote to Bill',N'Romania',               N'Bucharest',                                                                                                                1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 2 – Quote to Bill',N'Slovakia',              N'Bratislava',                                                                                                               1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 2 – Quote to Bill',N'South Africa',          N'Johannesburg',                                                                                                             1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 2 – Quote to Bill',N'Spain',                 N'Barcelona,Madrid',                                                                                                         1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 2 – Quote to Bill',N'Turkey',                N'Istanbul',                                                                                                                 1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 2 – Quote to Bill',N'United Arab Emirates',  N'Dubai',                                                                                                                    1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 2 – Quote to Bill',N'United Kingdom',        N'Slough',                                                                                                                   1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 2 – Quote to Bill',N'United States',         N'Atlanta',                                                                                                                  1,'2026-01-01');

    -- Lot 4 – One Post Sales
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Argentina',            N'Buenos Aires',                                                                                                             1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Australia',            N'Melbourne,Sydney,Perth',                                                                                                   1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Austria',              N'Vienna',                                                                                                                   1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Belgium',              N'Brussels',                                                                                                                 1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Brazil',               N'Indaial,Petropolis,Porto Alegre,Sao Paulo',                                                                                1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Canada',               N'Montreal,Toronto,Vancouver',                                                                                               1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Chile',                N'Santiago de Chile',                                                                                                        1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'China',                N'Beijing,Guangzhou,Shanghai,Shenzhen',                                                                                      1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Colombia',             N'Bogota',                                                                                                                   1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Czech Republic',       N'Prague',                                                                                                                   1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Denmark',              N'Copenhagen',                                                                                                               1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Egypt',                N'Cairo',                                                                                                                    1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Finland',              N'Helsinki',                                                                                                                 1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Germany',              N'Duesseldorf,Frankfurt,Hamburg,Munich,Stuttgart',                                                                           1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Greece',               N'Kallithea Attica',                                                                                                         1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Hong Kong',            N'Quarry Bay,Wanchai',                                                                                                       1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Hungary',              N'Bucharest',                                                                                                                1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'India',                N'Bangalore,Mumbai,New Delhi',                                                                                               1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Ireland',              N'Dublin',                                                                                                                   1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Italy',                N'Milan,Rome',                                                                                                               1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Japan',                N'Tokyo',                                                                                                                    1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Lebanon',              N'Beirut',                                                                                                                   1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Malaysia',             N'Kuala Lumpur',                                                                                                             1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Mexico',               N'Mexico City',                                                                                                              1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Netherlands',          N'Amsterdam',                                                                                                                1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'New Zealand',          N'Wellington',                                                                                                               1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Norway',               N'Oslo',                                                                                                                     1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Philippines',          N'Manila',                                                                                                                   1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Portugal',             N'Amadora',                                                                                                                  1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Qatar',                N'Doha',                                                                                                                     1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Saudi Arabia',         N'Riyadh',                                                                                                                   1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Singapore',            N'Singapore',                                                                                                                1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Slovakia',             N'Bratislava',                                                                                                               1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'South Africa',         N'Johannesburg',                                                                                                             1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Spain',                N'Barcelona,Madrid',                                                                                                         1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Sweden',               N'Solna',                                                                                                                    1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Switzerland',          N'Geneva,Zurich',                                                                                                            1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'Taiwan',               N'Taipei',                                                                                                                   1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'United Arab Emirates', N'Dubai',                                                                                                                    1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'United Kingdom',       N'Slough',                                                                                                                   1,'2026-01-01');
    INSERT INTO [LotCountryMappings] ([TenantId],[LotName],[Country],[Cities],[IsActive],[CreatedAt]) VALUES (1,N'Lot 4 – One Post Sales',N'United States',        N'Atlanta,Chicago,Columbia,Dallas,Denver,Los Angeles,Manchester,Miami,Montgomery,New York,Oak Hill,Phoenix,Reston,San Francisco,St Paul,St Petersburg,Tulsa,Warren',1,'2026-01-01');
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260309101022_SeedLotCountryMappings')
    INSERT INTO [__EFMigrationsHistory] VALUES (N'20260309101022_SeedLotCountryMappings', N'8.0.13');
GO

COMMIT;
GO

-- =============================================================================
-- 20260310052625_AddIntakeFieldConfig
-- =============================================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260310052625_AddIntakeFieldConfig')
BEGIN
    CREATE TABLE [IntakeFieldConfigs] (
        [Id]           int           NOT NULL IDENTITY,
        [TenantId]     int           NOT NULL,
        [FieldName]    nvarchar(max) NOT NULL,
        [DisplayName]  nvarchar(max) NOT NULL,
        [SectionName]  nvarchar(max) NOT NULL,
        [IsVisible]    bit           NOT NULL,
        [IsMandatory]  bit           NOT NULL,
        [DisplayOrder] int           NOT NULL,
        CONSTRAINT [PK_IntakeFieldConfigs] PRIMARY KEY ([Id])
    );

    INSERT INTO [IntakeFieldConfigs] ([TenantId],[FieldName],[DisplayName],[SectionName],[IsVisible],[IsMandatory],[DisplayOrder]) VALUES (1,N'ProcessName',         N'Process Name',          N'Process Information',      1,1, 1);
    INSERT INTO [IntakeFieldConfigs] ([TenantId],[FieldName],[DisplayName],[SectionName],[IsVisible],[IsMandatory],[DisplayOrder]) VALUES (1,N'Description',         N'Description',           N'Process Information',      1,1, 2);
    INSERT INTO [IntakeFieldConfigs] ([TenantId],[FieldName],[DisplayName],[SectionName],[IsVisible],[IsMandatory],[DisplayOrder]) VALUES (1,N'ProcessType',         N'Process Type',          N'Process Information',      1,0, 3);
    INSERT INTO [IntakeFieldConfigs] ([TenantId],[FieldName],[DisplayName],[SectionName],[IsVisible],[IsMandatory],[DisplayOrder]) VALUES (1,N'Priority',            N'Priority',              N'Process Information',      1,0, 4);
    INSERT INTO [IntakeFieldConfigs] ([TenantId],[FieldName],[DisplayName],[SectionName],[IsVisible],[IsMandatory],[DisplayOrder]) VALUES (1,N'EstimatedVolumePerDay',N'Est. Volume / Day',    N'Process Information',      1,0, 5);
    INSERT INTO [IntakeFieldConfigs] ([TenantId],[FieldName],[DisplayName],[SectionName],[IsVisible],[IsMandatory],[DisplayOrder]) VALUES (1,N'BusinessUnit',        N'Business Unit',         N'Ownership & Organisation', 1,0, 6);
    INSERT INTO [IntakeFieldConfigs] ([TenantId],[FieldName],[DisplayName],[SectionName],[IsVisible],[IsMandatory],[DisplayOrder]) VALUES (1,N'Department',          N'Department',            N'Ownership & Organisation', 1,0, 7);
    INSERT INTO [IntakeFieldConfigs] ([TenantId],[FieldName],[DisplayName],[SectionName],[IsVisible],[IsMandatory],[DisplayOrder]) VALUES (1,N'Lob',                 N'Line of Business (LOB)',N'Ownership & Organisation', 1,0, 8);
    INSERT INTO [IntakeFieldConfigs] ([TenantId],[FieldName],[DisplayName],[SectionName],[IsVisible],[IsMandatory],[DisplayOrder]) VALUES (1,N'SdcLots',             N'Lots or SDC',           N'Ownership & Organisation', 1,0, 9);
    INSERT INTO [IntakeFieldConfigs] ([TenantId],[FieldName],[DisplayName],[SectionName],[IsVisible],[IsMandatory],[DisplayOrder]) VALUES (1,N'ProcessOwnerName',    N'Process Owner Name',    N'Ownership & Organisation', 1,0,10);
    INSERT INTO [IntakeFieldConfigs] ([TenantId],[FieldName],[DisplayName],[SectionName],[IsVisible],[IsMandatory],[DisplayOrder]) VALUES (1,N'ProcessOwnerEmail',   N'Process Owner Email',   N'Ownership & Organisation', 1,0,11);
    INSERT INTO [IntakeFieldConfigs] ([TenantId],[FieldName],[DisplayName],[SectionName],[IsVisible],[IsMandatory],[DisplayOrder]) VALUES (1,N'Country',             N'Country',               N'Location',                 1,0,12);
    INSERT INTO [IntakeFieldConfigs] ([TenantId],[FieldName],[DisplayName],[SectionName],[IsVisible],[IsMandatory],[DisplayOrder]) VALUES (1,N'City',                N'City',                  N'Location',                 1,0,13);
    INSERT INTO [IntakeFieldConfigs] ([TenantId],[FieldName],[DisplayName],[SectionName],[IsVisible],[IsMandatory],[DisplayOrder]) VALUES (1,N'SiteLocation',        N'Site / Office Location', N'Location',                1,0,14);
    INSERT INTO [IntakeFieldConfigs] ([TenantId],[FieldName],[DisplayName],[SectionName],[IsVisible],[IsMandatory],[DisplayOrder]) VALUES (1,N'TimeZone',            N'Time Zone',             N'Location',                 1,0,15);
    INSERT INTO [IntakeFieldConfigs] ([TenantId],[FieldName],[DisplayName],[SectionName],[IsVisible],[IsMandatory],[DisplayOrder]) VALUES (1,N'Document',            N'Document Upload',       N'Documents',                1,0,16);
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260310052625_AddIntakeFieldConfig')
    INSERT INTO [__EFMigrationsHistory] VALUES (N'20260310052625_AddIntakeFieldConfig', N'8.0.13');
GO

COMMIT;
GO

-- =============================================================================
-- 20260310155450_AddTenantPiiSettings
-- =============================================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260310155450_AddTenantPiiSettings')
BEGIN
    CREATE TABLE [TenantPiiSettings] (
        [Id]                    int           NOT NULL IDENTITY,
        [TenantId]              int           NOT NULL,
        [IsEnabled]             bit           NOT NULL,
        [BlockOnDetection]      bit           NOT NULL,
        [DetectEmailAddresses]  bit           NOT NULL,
        [DetectPhoneNumbers]    bit           NOT NULL,
        [DetectCreditCardNumbers] bit         NOT NULL,
        [DetectSsnNumbers]      bit           NOT NULL,
        [DetectIpAddresses]     bit           NOT NULL,
        [DetectPassportNumbers] bit           NOT NULL,
        [DetectDatesOfBirth]    bit           NOT NULL,
        [DetectUrls]            bit           NOT NULL,
        [DetectPersonNames]     bit           NOT NULL,
        [UpdatedAt]             datetime2     NOT NULL,
        [UpdatedByUserId]       nvarchar(max) NULL,
        CONSTRAINT [PK_TenantPiiSettings] PRIMARY KEY ([Id])
    );

    CREATE UNIQUE INDEX [IX_TenantPiiSettings_TenantId] ON [TenantPiiSettings] ([TenantId]);
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260310155450_AddTenantPiiSettings')
    INSERT INTO [__EFMigrationsHistory] VALUES (N'20260310155450_AddTenantPiiSettings', N'8.0.13');
GO

COMMIT;
GO
-- =============================================================================
-- 20260311000000_FixAllTablesIdentity
-- Repairs ALL int-PK tables that were created without IDENTITY(1,1) on SQL
-- Server because early migrations only carried Sqlite:Autoincrement.
-- Each block is a no-op when IDENTITY already exists on the table.
-- This migration is NEW — it will run even on databases that already have all
-- previous migrations in __EFMigrationsHistory.
-- =============================================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260311000000_FixAllTablesIdentity')
BEGIN
    -- AuthSettings
    IF OBJECT_ID(N'[AuthSettings]', N'U') IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM sys.identity_columns ic JOIN sys.tables t ON ic.object_id=t.object_id WHERE t.name='AuthSettings' AND ic.name='Id')
    BEGIN
        DROP TABLE [AuthSettings];
        CREATE TABLE [AuthSettings] (
            [Id]               int           NOT NULL IDENTITY(1,1),
            [AuthMode]         nvarchar(max) NOT NULL,
            [LdapServer]       nvarchar(max) NULL,
            [LdapPort]         int           NOT NULL,
            [LdapBaseDn]       nvarchar(max) NULL,
            [LdapBindDn]       nvarchar(max) NULL,
            [LdapBindPassword] nvarchar(max) NULL,
            [LdapUseSsl]       bit           NOT NULL,
            [LdapSearchFilter] nvarchar(max) NULL,
            [TenantId]         int           NOT NULL DEFAULT 1,
            [UpdatedAt]        datetime2     NOT NULL,
            CONSTRAINT [PK_AuthSettings] PRIMARY KEY ([Id])
        );
    END

    -- MasterDepartments
    IF OBJECT_ID(N'[MasterDepartments]', N'U') IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM sys.identity_columns ic JOIN sys.tables t ON ic.object_id=t.object_id WHERE t.name='MasterDepartments' AND ic.name='Id')
    BEGIN
        DROP TABLE [MasterDepartments];
        CREATE TABLE [MasterDepartments] (
            [Id]          int           NOT NULL IDENTITY(1,1),
            [TenantId]    int           NOT NULL DEFAULT 1,
            [Name]        nvarchar(max) NOT NULL,
            [Description] nvarchar(max) NULL,
            [IsActive]    bit           NOT NULL,
            [CreatedAt]   datetime2     NOT NULL,
            CONSTRAINT [PK_MasterDepartments] PRIMARY KEY ([Id])
        );
    END

    -- MasterLobs
    IF OBJECT_ID(N'[MasterLobs]', N'U') IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM sys.identity_columns ic JOIN sys.tables t ON ic.object_id=t.object_id WHERE t.name='MasterLobs' AND ic.name='Id')
    BEGIN
        DROP TABLE [MasterLobs];
        CREATE TABLE [MasterLobs] (
            [Id]             int           NOT NULL IDENTITY(1,1),
            [TenantId]       int           NOT NULL,
            [DepartmentName] nvarchar(max) NOT NULL,
            [Name]           nvarchar(max) NOT NULL,
            [Description]    nvarchar(max) NULL,
            [IsActive]       bit           NOT NULL,
            [CreatedAt]      datetime2     NOT NULL,
            CONSTRAINT [PK_MasterLobs] PRIMARY KEY ([Id])
        );
    END

    -- Tenants (the table currently causing the reported failure)
    IF OBJECT_ID(N'[Tenants]', N'U') IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM sys.identity_columns ic JOIN sys.tables t ON ic.object_id=t.object_id WHERE t.name='Tenants' AND ic.name='Id')
    BEGIN
        -- Drop dependent tables first
        IF OBJECT_ID(N'[UserTenants]',      N'U') IS NOT NULL DROP TABLE [UserTenants];
        IF OBJECT_ID(N'[TenantAiSettings]', N'U') IS NOT NULL DROP TABLE [TenantAiSettings];
        IF OBJECT_ID(N'[TenantPiiSettings]',N'U') IS NOT NULL DROP TABLE [TenantPiiSettings];
        DROP TABLE [Tenants];
        CREATE TABLE [Tenants] (
            [Id]          int           NOT NULL IDENTITY(1,1),
            [Name]        nvarchar(max) NOT NULL,
            [Slug]        nvarchar(max) NOT NULL,
            [Color]       nvarchar(max) NOT NULL,
            [Description] nvarchar(max) NULL,
            [IsActive]    bit           NOT NULL,
            [CreatedAt]   datetime2     NOT NULL,
            CONSTRAINT [PK_Tenants] PRIMARY KEY ([Id])
        );
        CREATE TABLE [TenantAiSettings] (
            [Id]                           int           NOT NULL IDENTITY(1,1),
            [TenantId]                     int           NOT NULL,
            [AzureOpenAIEndpoint]          nvarchar(max) NOT NULL,
            [AzureOpenAIApiKey]            nvarchar(max) NOT NULL,
            [AzureOpenAIDeploymentName]    nvarchar(max) NOT NULL,
            [AzureOpenAIApiVersion]        nvarchar(max) NOT NULL,
            [AzureOpenAIMaxTokens]         int           NOT NULL,
            [AzureStorageConnectionString] nvarchar(max) NOT NULL,
            [AzureStorageContainerName]    nvarchar(max) NOT NULL,
            [AzureSpeechApiKey]            nvarchar(max) NOT NULL DEFAULT N'',
            [AzureSpeechRegion]            nvarchar(max) NOT NULL DEFAULT N'',
            [UseCountryFilterByLot]        bit           NOT NULL DEFAULT CAST(0 AS bit),
            [UpdatedAt]                    datetime2     NOT NULL,
            [UpdatedByUserId]              nvarchar(max) NULL,
            CONSTRAINT [PK_TenantAiSettings] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_TenantAiSettings_Tenants_TenantId]
                FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE CASCADE
        );
        CREATE UNIQUE INDEX [IX_TenantAiSettings_TenantId] ON [TenantAiSettings] ([TenantId]);
        CREATE TABLE [UserTenants] (
            [Id]         int           NOT NULL IDENTITY(1,1),
            [UserId]     nvarchar(450) NOT NULL,
            [TenantId]   int           NOT NULL,
            [TenantRole] nvarchar(max) NOT NULL,
            [IsDefault]  bit           NOT NULL,
            [AssignedAt] datetime2     NOT NULL,
            CONSTRAINT [PK_UserTenants] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_UserTenants_AspNetUsers_UserId]
                FOREIGN KEY ([UserId])   REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE,
            CONSTRAINT [FK_UserTenants_Tenants_TenantId]
                FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
        );
        CREATE INDEX [IX_UserTenants_TenantId] ON [UserTenants] ([TenantId]);
        CREATE INDEX [IX_UserTenants_UserId]   ON [UserTenants] ([UserId]);
        CREATE TABLE [TenantPiiSettings] (
            [Id]                      int           NOT NULL IDENTITY(1,1),
            [TenantId]                int           NOT NULL,
            [IsEnabled]               bit           NOT NULL,
            [BlockOnDetection]        bit           NOT NULL,
            [DetectEmailAddresses]    bit           NOT NULL,
            [DetectPhoneNumbers]      bit           NOT NULL,
            [DetectCreditCardNumbers] bit           NOT NULL,
            [DetectSsnNumbers]        bit           NOT NULL,
            [DetectIpAddresses]       bit           NOT NULL,
            [DetectPassportNumbers]   bit           NOT NULL,
            [DetectDatesOfBirth]      bit           NOT NULL,
            [DetectUrls]              bit           NOT NULL,
            [DetectPersonNames]       bit           NOT NULL,
            [UpdatedAt]               datetime2     NOT NULL,
            [UpdatedByUserId]         nvarchar(max) NULL,
            CONSTRAINT [PK_TenantPiiSettings] PRIMARY KEY ([Id])
        );
        CREATE UNIQUE INDEX [IX_TenantPiiSettings_TenantId] ON [TenantPiiSettings] ([TenantId]);
    END

    -- TenantAiSettings (fix independently if Tenants was already correct)
    IF OBJECT_ID(N'[TenantAiSettings]', N'U') IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM sys.identity_columns ic JOIN sys.tables t ON ic.object_id=t.object_id WHERE t.name='TenantAiSettings' AND ic.name='Id')
    BEGIN
        DROP TABLE [TenantAiSettings];
        CREATE TABLE [TenantAiSettings] (
            [Id]                           int           NOT NULL IDENTITY(1,1),
            [TenantId]                     int           NOT NULL,
            [AzureOpenAIEndpoint]          nvarchar(max) NOT NULL,
            [AzureOpenAIApiKey]            nvarchar(max) NOT NULL,
            [AzureOpenAIDeploymentName]    nvarchar(max) NOT NULL,
            [AzureOpenAIApiVersion]        nvarchar(max) NOT NULL,
            [AzureOpenAIMaxTokens]         int           NOT NULL,
            [AzureStorageConnectionString] nvarchar(max) NOT NULL,
            [AzureStorageContainerName]    nvarchar(max) NOT NULL,
            [AzureSpeechApiKey]            nvarchar(max) NOT NULL DEFAULT N'',
            [AzureSpeechRegion]            nvarchar(max) NOT NULL DEFAULT N'',
            [UseCountryFilterByLot]        bit           NOT NULL DEFAULT CAST(0 AS bit),
            [UpdatedAt]                    datetime2     NOT NULL,
            [UpdatedByUserId]              nvarchar(max) NULL,
            CONSTRAINT [PK_TenantAiSettings] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_TenantAiSettings_Tenants_TenantId]
                FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE CASCADE
        );
        CREATE UNIQUE INDEX [IX_TenantAiSettings_TenantId] ON [TenantAiSettings] ([TenantId]);
    END

    -- UserTenants (fix independently if Tenants was already correct)
    IF OBJECT_ID(N'[UserTenants]', N'U') IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM sys.identity_columns ic JOIN sys.tables t ON ic.object_id=t.object_id WHERE t.name='UserTenants' AND ic.name='Id')
    BEGIN
        DROP TABLE [UserTenants];
        CREATE TABLE [UserTenants] (
            [Id]         int           NOT NULL IDENTITY(1,1),
            [UserId]     nvarchar(450) NOT NULL,
            [TenantId]   int           NOT NULL,
            [TenantRole] nvarchar(max) NOT NULL,
            [IsDefault]  bit           NOT NULL,
            [AssignedAt] datetime2     NOT NULL,
            CONSTRAINT [PK_UserTenants] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_UserTenants_AspNetUsers_UserId]
                FOREIGN KEY ([UserId])   REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE,
            CONSTRAINT [FK_UserTenants_Tenants_TenantId]
                FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
        );
        CREATE INDEX [IX_UserTenants_TenantId] ON [UserTenants] ([TenantId]);
        CREATE INDEX [IX_UserTenants_UserId]   ON [UserTenants] ([UserId]);
    END

    -- TenantPiiSettings (fix independently if Tenants was already correct)
    IF OBJECT_ID(N'[TenantPiiSettings]', N'U') IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM sys.identity_columns ic JOIN sys.tables t ON ic.object_id=t.object_id WHERE t.name='TenantPiiSettings' AND ic.name='Id')
    BEGIN
        DROP TABLE [TenantPiiSettings];
        CREATE TABLE [TenantPiiSettings] (
            [Id]                      int           NOT NULL IDENTITY(1,1),
            [TenantId]                int           NOT NULL,
            [IsEnabled]               bit           NOT NULL,
            [BlockOnDetection]        bit           NOT NULL,
            [DetectEmailAddresses]    bit           NOT NULL,
            [DetectPhoneNumbers]      bit           NOT NULL,
            [DetectCreditCardNumbers] bit           NOT NULL,
            [DetectSsnNumbers]        bit           NOT NULL,
            [DetectIpAddresses]       bit           NOT NULL,
            [DetectPassportNumbers]   bit           NOT NULL,
            [DetectDatesOfBirth]      bit           NOT NULL,
            [DetectUrls]              bit           NOT NULL,
            [DetectPersonNames]       bit           NOT NULL,
            [UpdatedAt]               datetime2     NOT NULL,
            [UpdatedByUserId]         nvarchar(max) NULL,
            CONSTRAINT [PK_TenantPiiSettings] PRIMARY KEY ([Id])
        );
        CREATE UNIQUE INDEX [IX_TenantPiiSettings_TenantId] ON [TenantPiiSettings] ([TenantId]);
    END

    -- AspNetRoleClaims
    IF OBJECT_ID(N'[AspNetRoleClaims]', N'U') IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM sys.identity_columns ic JOIN sys.tables t ON ic.object_id=t.object_id WHERE t.name='AspNetRoleClaims' AND ic.name='Id')
    BEGIN
        DROP TABLE [AspNetRoleClaims];
        CREATE TABLE [AspNetRoleClaims] (
            [Id]         int           NOT NULL IDENTITY(1,1),
            [RoleId]     nvarchar(450) NOT NULL,
            [ClaimType]  nvarchar(max) NULL,
            [ClaimValue] nvarchar(max) NULL,
            CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId]
                FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
        );
        CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
    END

    -- AspNetUserClaims
    IF OBJECT_ID(N'[AspNetUserClaims]', N'U') IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM sys.identity_columns ic JOIN sys.tables t ON ic.object_id=t.object_id WHERE t.name='AspNetUserClaims' AND ic.name='Id')
    BEGIN
        DROP TABLE [AspNetUserClaims];
        CREATE TABLE [AspNetUserClaims] (
            [Id]         int           NOT NULL IDENTITY(1,1),
            [UserId]     nvarchar(450) NOT NULL,
            [ClaimType]  nvarchar(max) NULL,
            [ClaimValue] nvarchar(max) NULL,
            CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId]
                FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
        );
        CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
    END

    -- IntakeRecords + all child tables in one block
    IF OBJECT_ID(N'[IntakeRecords]', N'U') IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM sys.identity_columns ic JOIN sys.tables t ON ic.object_id=t.object_id WHERE t.name='IntakeRecords' AND ic.name='Id')
    BEGIN
        IF OBJECT_ID(N'[IntakeDocuments]',     N'U') IS NOT NULL DROP TABLE [IntakeDocuments];
        IF OBJECT_ID(N'[TaskActionLogs]',      N'U') IS NOT NULL DROP TABLE [TaskActionLogs];
        IF OBJECT_ID(N'[IntakeTasks]',         N'U') IS NOT NULL DROP TABLE [IntakeTasks];
        IF OBJECT_ID(N'[ReportFieldStatuses]', N'U') IS NOT NULL DROP TABLE [ReportFieldStatuses];
        IF OBJECT_ID(N'[FinalReports]',        N'U') IS NOT NULL DROP TABLE [FinalReports];
        IF OBJECT_ID(N'[QcChecks]',            N'U') IS NOT NULL DROP TABLE [QcChecks];
        IF OBJECT_ID(N'[RagJobs]',             N'U') IS NOT NULL DROP TABLE [RagJobs];
        DROP TABLE [IntakeRecords];
        CREATE TABLE [IntakeRecords] (
            [Id]                      int           NOT NULL IDENTITY(1,1),
            [IntakeId]                nvarchar(max) NOT NULL,
            [ProcessName]             nvarchar(max) NOT NULL,
            [Description]             nvarchar(max) NOT NULL,
            [BusinessUnit]            nvarchar(max) NOT NULL,
            [Department]              nvarchar(max) NOT NULL,
            [ProcessOwnerName]        nvarchar(max) NOT NULL,
            [ProcessOwnerEmail]       nvarchar(max) NOT NULL,
            [ProcessType]             nvarchar(max) NOT NULL,
            [EstimatedVolumePerDay]   int           NOT NULL,
            [Priority]                nvarchar(max) NOT NULL,
            [Country]                 nvarchar(max) NOT NULL,
            [City]                    nvarchar(max) NOT NULL,
            [SiteLocation]            nvarchar(max) NOT NULL,
            [TimeZone]                nvarchar(max) NOT NULL,
            [UploadedFileName]        nvarchar(max) NULL,
            [UploadedFilePath]        nvarchar(max) NULL,
            [UploadedFileContentType] nvarchar(max) NULL,
            [UploadedFileSize]        bigint        NULL,
            [Status]                  nvarchar(max) NOT NULL,
            [AnalysisResult]          nvarchar(max) NULL,
            [CreatedAt]               datetime2     NOT NULL,
            [SubmittedAt]             datetime2     NULL,
            [AnalyzedAt]              datetime2     NULL,
            [CreatedByUserId]         nvarchar(max) NULL,
            [TenantId]                int           NOT NULL DEFAULT 1,
            [Lob]                     nvarchar(max) NOT NULL DEFAULT N'',
            [SdcLots]                 nvarchar(max) NOT NULL DEFAULT N'',
            CONSTRAINT [PK_IntakeRecords] PRIMARY KEY ([Id])
        );
        CREATE TABLE [FinalReports] (
            [Id]                int           NOT NULL IDENTITY(1,1),
            [IntakeRecordId]    int           NOT NULL,
            [ReportFileName]    nvarchar(max) NOT NULL,
            [FilePath]          nvarchar(max) NOT NULL,
            [FileSizeBytes]     bigint        NOT NULL,
            [GeneratedAt]       datetime2     NOT NULL,
            [GeneratedByUserId] nvarchar(max) NULL,
            [GeneratedByName]   nvarchar(max) NULL,
            CONSTRAINT [PK_FinalReports] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_FinalReports_IntakeRecords_IntakeRecordId]
                FOREIGN KEY ([IntakeRecordId]) REFERENCES [IntakeRecords] ([Id]) ON DELETE CASCADE
        );
        CREATE INDEX [IX_FinalReports_IntakeRecordId] ON [FinalReports] ([IntakeRecordId]);
        CREATE TABLE [IntakeTasks] (
            [Id]              int           NOT NULL IDENTITY(1,1),
            [TaskId]          nvarchar(max) NOT NULL,
            [IntakeRecordId]  int           NOT NULL,
            [Title]           nvarchar(max) NOT NULL,
            [Description]     nvarchar(max) NOT NULL,
            [Owner]           nvarchar(max) NOT NULL,
            [Priority]        nvarchar(max) NOT NULL,
            [Status]          nvarchar(max) NOT NULL,
            [CreatedAt]       datetime2     NOT NULL,
            [DueDate]         datetime2     NOT NULL,
            [CompletedAt]     datetime2     NULL,
            [CreatedByUserId] nvarchar(max) NULL,
            [IsNotApplicable] bit           NOT NULL DEFAULT CAST(0 AS bit),
            [NaReason]        nvarchar(max) NULL,
            CONSTRAINT [PK_IntakeTasks] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_IntakeTasks_IntakeRecords_IntakeRecordId]
                FOREIGN KEY ([IntakeRecordId]) REFERENCES [IntakeRecords] ([Id]) ON DELETE CASCADE
        );
        CREATE INDEX [IX_IntakeTasks_IntakeRecordId] ON [IntakeTasks] ([IntakeRecordId]);
        CREATE TABLE [IntakeDocuments] (
            [Id]               int           NOT NULL IDENTITY(1,1),
            [IntakeRecordId]   int           NOT NULL,
            [IntakeTaskId]     int           NULL,
            [FileName]         nvarchar(max) NOT NULL,
            [FilePath]         nvarchar(max) NOT NULL,
            [ContentType]      nvarchar(max) NULL,
            [FileSize]         bigint        NULL,
            [DocumentType]     nvarchar(max) NOT NULL,
            [UploadedAt]       datetime2     NOT NULL,
            [UploadedByUserId] nvarchar(max) NULL,
            [UploadedByName]   nvarchar(max) NULL,
            [SopDocumentPath]  nvarchar(max) NULL,
            [TranscriptStatus] nvarchar(max) NOT NULL DEFAULT N'',
            [TranscriptText]   nvarchar(max) NULL,
            CONSTRAINT [PK_IntakeDocuments] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_IntakeDocuments_IntakeRecords_IntakeRecordId]
                FOREIGN KEY ([IntakeRecordId]) REFERENCES [IntakeRecords] ([Id]) ON DELETE CASCADE,
            CONSTRAINT [FK_IntakeDocuments_IntakeTasks_IntakeTaskId]
                FOREIGN KEY ([IntakeTaskId]) REFERENCES [IntakeTasks] ([Id])
        );
        CREATE INDEX [IX_IntakeDocuments_IntakeRecordId] ON [IntakeDocuments] ([IntakeRecordId]);
        CREATE INDEX [IX_IntakeDocuments_IntakeTaskId]   ON [IntakeDocuments] ([IntakeTaskId]);
        CREATE TABLE [TaskActionLogs] (
            [Id]              int           NOT NULL IDENTITY(1,1),
            [IntakeTaskId]    int           NOT NULL,
            [ActionType]      nvarchar(max) NOT NULL,
            [OldStatus]       nvarchar(max) NULL,
            [NewStatus]       nvarchar(max) NULL,
            [Comment]         nvarchar(max) NULL,
            [CreatedAt]       datetime2     NOT NULL,
            [CreatedByUserId] nvarchar(max) NULL,
            [CreatedByName]   nvarchar(max) NULL,
            CONSTRAINT [PK_TaskActionLogs] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_TaskActionLogs_IntakeTasks_IntakeTaskId]
                FOREIGN KEY ([IntakeTaskId]) REFERENCES [IntakeTasks] ([Id]) ON DELETE CASCADE
        );
        CREATE INDEX [IX_TaskActionLogs_IntakeTaskId] ON [TaskActionLogs] ([IntakeTaskId]);
        CREATE TABLE [ReportFieldStatuses] (
            [Id]                  int           NOT NULL IDENTITY(1,1),
            [IntakeRecordId]      int           NOT NULL,
            [FieldKey]            nvarchar(max) NOT NULL,
            [FieldLabel]          nvarchar(max) NOT NULL,
            [Section]             nvarchar(max) NOT NULL,
            [TemplatePlaceholder] nvarchar(max) NOT NULL,
            [Status]              nvarchar(max) NOT NULL,
            [FillValue]           nvarchar(max) NULL,
            [IsNA]                bit           NOT NULL,
            [Notes]               nvarchar(max) NULL,
            [LinkedTaskId]        nvarchar(max) NULL,
            [AnalyzedAt]          datetime2     NULL,
            [UpdatedAt]           datetime2     NOT NULL,
            CONSTRAINT [PK_ReportFieldStatuses] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_ReportFieldStatuses_IntakeRecords_IntakeRecordId]
                FOREIGN KEY ([IntakeRecordId]) REFERENCES [IntakeRecords] ([Id]) ON DELETE CASCADE
        );
        CREATE INDEX [IX_ReportFieldStatuses_IntakeRecordId] ON [ReportFieldStatuses] ([IntakeRecordId]);
        CREATE TABLE [QcChecks] (
            [Id]                 int           NOT NULL IDENTITY(1,1),
            [TenantId]           int           NOT NULL DEFAULT 1,
            [IntakeRecordId]     int           NOT NULL,
            [OverallScore]       int           NOT NULL,
            [ScoreBreakdownJson] nvarchar(max) NULL,
            [Status]             nvarchar(max) NOT NULL,
            [ErrorMessage]       nvarchar(max) NULL,
            [CreatedAt]          datetime2     NOT NULL,
            [CompletedAt]        datetime2     NULL,
            [RunByUserId]        nvarchar(max) NULL,
            CONSTRAINT [PK_QcChecks] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_QcChecks_IntakeRecords_IntakeRecordId]
                FOREIGN KEY ([IntakeRecordId]) REFERENCES [IntakeRecords] ([Id]) ON DELETE CASCADE
        );
        CREATE INDEX [IX_QcChecks_IntakeRecordId] ON [QcChecks] ([IntakeRecordId]);
        CREATE TABLE [RagJobs] (
            [Id]             int           NOT NULL IDENTITY(1,1),
            [IntakeRecordId] int           NOT NULL,
            [Status]         nvarchar(max) NOT NULL,
            [TotalFiles]     int           NOT NULL,
            [ProcessedFiles] int           NOT NULL,
            [ErrorMessage]   nvarchar(max) NULL,
            [CreatedAt]      datetime2     NOT NULL,
            [StartedAt]      datetime2     NULL,
            [CompletedAt]    datetime2     NULL,
            [NotifyUserId]   nvarchar(max) NULL,
            CONSTRAINT [PK_RagJobs] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_RagJobs_IntakeRecords_IntakeRecordId]
                FOREIGN KEY ([IntakeRecordId]) REFERENCES [IntakeRecords] ([Id]) ON DELETE CASCADE
        );
        CREATE INDEX [IX_RagJobs_IntakeRecordId] ON [RagJobs] ([IntakeRecordId]);
    END

    -- IntakeFieldConfigs
    IF OBJECT_ID(N'[IntakeFieldConfigs]', N'U') IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM sys.identity_columns ic JOIN sys.tables t ON ic.object_id=t.object_id WHERE t.name='IntakeFieldConfigs' AND ic.name='Id')
    BEGIN
        DROP TABLE [IntakeFieldConfigs];
        CREATE TABLE [IntakeFieldConfigs] (
            [Id]           int           NOT NULL IDENTITY(1,1),
            [TenantId]     int           NOT NULL,
            [FieldName]    nvarchar(max) NOT NULL,
            [DisplayName]  nvarchar(max) NOT NULL,
            [SectionName]  nvarchar(max) NOT NULL,
            [IsVisible]    bit           NOT NULL,
            [IsMandatory]  bit           NOT NULL,
            [DisplayOrder] int           NOT NULL,
            CONSTRAINT [PK_IntakeFieldConfigs] PRIMARY KEY ([Id])
        );
    END
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260311000000_FixAllTablesIdentity')
    INSERT INTO [__EFMigrationsHistory] VALUES (N'20260311000000_FixAllTablesIdentity', N'8.0.13');
GO

COMMIT;
GO

-- =============================================================================
-- 20260312035530_AddPiiMaskingLog
-- =============================================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260312035530_AddPiiMaskingLog')
BEGIN
    -- Add PiiMaskingLog column to IntakeRecords if it does not already exist
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns c
        JOIN sys.tables t ON c.object_id = t.object_id
        WHERE t.name = N'IntakeRecords' AND c.name = N'PiiMaskingLog'
    )
        ALTER TABLE [IntakeRecords] ADD [PiiMaskingLog] nvarchar(max) NULL;
END
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260312035530_AddPiiMaskingLog')
    INSERT INTO [__EFMigrationsHistory] VALUES (N'20260312035530_AddPiiMaskingLog', N'8.0.13');
GO

COMMIT;
GO

-- =============================================================================
-- 20260313050649_AddAuditLogs
-- =============================================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260313050649_AddAuditLogs')
BEGIN
    IF OBJECT_ID(N'[AuditLogs]', N'U') IS NULL
    BEGIN
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
    END
END
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260313050649_AddAuditLogs')
    INSERT INTO [__EFMigrationsHistory] VALUES (N'20260313050649_AddAuditLogs', N'8.0.13');
GO

COMMIT;
GO

-- =============================================================================
-- 20260313073600_FixAuditLogsColumnTypes
-- Repairs the AuditLogs table in case it was created with SQLite types (TEXT/INTEGER)
-- instead of SQL Server types (nvarchar/bit/bigint/IDENTITY).
-- If the table was already created with correct types above, the guard prevents
-- the DROP+CREATE so this block becomes a no-op.
-- =============================================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260313073600_FixAuditLogsColumnTypes')
BEGIN
    IF OBJECT_ID(N'[AuditLogs]', N'U') IS NOT NULL
        AND NOT EXISTS (
            SELECT 1 FROM sys.identity_columns ic
            JOIN sys.tables t ON ic.object_id = t.object_id
            WHERE t.name = N'AuditLogs' AND ic.name = N'Id'
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
    END
END
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260313073600_FixAuditLogsColumnTypes')
    INSERT INTO [__EFMigrationsHistory] VALUES (N'20260313073600_FixAuditLogsColumnTypes', N'8.0.13');
GO

COMMIT;
GO

-- =============================================================================
-- 20260316063823_AddModelVersionToTenantAiSettings
-- Adds the AzureOpenAIModelVersion column so administrators can choose between
-- GPT-4o and GPT-5.2 per tenant.  Existing rows default to 'gpt-5.2'.
-- =============================================================================
BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260316063823_AddModelVersionToTenantAiSettings')
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns c
        JOIN sys.tables t ON c.object_id = t.object_id
        WHERE t.name = N'TenantAiSettings' AND c.name = N'AzureOpenAIModelVersion'
    )
        ALTER TABLE [TenantAiSettings]
            ADD [AzureOpenAIModelVersion] nvarchar(max) NOT NULL
                CONSTRAINT [DF_TenantAiSettings_AzureOpenAIModelVersion] DEFAULT N'gpt-5.2';
END
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260316063823_AddModelVersionToTenantAiSettings')
    INSERT INTO [__EFMigrationsHistory] VALUES (N'20260316063823_AddModelVersionToTenantAiSettings', N'8.0.13');
GO

COMMIT;
GO
-- =============================================================================
-- End of script
-- =============================================================================
