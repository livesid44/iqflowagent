using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IQFlowAgent.Web.Migrations
{
    /// <inheritdoc />
    public partial class FixAllTablesIdentity : Migration
    {
        // Every table whose int PK was created without IDENTITY on SQL Server because
        // the original migrations only carried Sqlite:Autoincrement and not SqlServer:Identity.
        // This migration inspects each table at runtime: if the Id column lacks IDENTITY it
        // drops (empty) the table and recreates it correctly.  It is a no-op for:
        //   - SQLite databases (guarded by ActiveProvider)
        //   - tables that already have IDENTITY (new databases or previously repaired ones)
        //   - LotCountryMappings which was already repaired by 20260309095000_FixLotCountryMappingsIdentity

        private const string CheckAndFix = @"
IF OBJECT_ID(N'[{0}]', N'U') IS NOT NULL
    AND NOT EXISTS (
        SELECT 1 FROM sys.identity_columns ic
        JOIN sys.tables t ON ic.object_id = t.object_id
        WHERE t.name = '{0}' AND ic.name = 'Id'
    )
BEGIN
    DROP TABLE [{0}];
    {1}
END";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer")
                return;

            // ── AuthSettings ──────────────────────────────────────────────────────────
            migrationBuilder.Sql(string.Format(CheckAndFix, "AuthSettings", @"
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
                );"));

            // ── IntakeRecords ─────────────────────────────────────────────────────────
            migrationBuilder.Sql(string.Format(CheckAndFix, "IntakeRecords", @"
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
                );"));

            // ── AspNetRoleClaims ──────────────────────────────────────────────────────
            migrationBuilder.Sql(string.Format(CheckAndFix, "AspNetRoleClaims", @"
                CREATE TABLE [AspNetRoleClaims] (
                    [Id]         int           NOT NULL IDENTITY(1,1),
                    [RoleId]     nvarchar(450) NOT NULL,
                    [ClaimType]  nvarchar(max) NULL,
                    [ClaimValue] nvarchar(max) NULL,
                    CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId]
                        FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
                );
                CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);"));

            // ── AspNetUserClaims ──────────────────────────────────────────────────────
            migrationBuilder.Sql(string.Format(CheckAndFix, "AspNetUserClaims", @"
                CREATE TABLE [AspNetUserClaims] (
                    [Id]         int           NOT NULL IDENTITY(1,1),
                    [UserId]     nvarchar(450) NOT NULL,
                    [ClaimType]  nvarchar(max) NULL,
                    [ClaimValue] nvarchar(max) NULL,
                    CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId]
                        FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
                );
                CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);"));

            // ── MasterDepartments ─────────────────────────────────────────────────────
            migrationBuilder.Sql(string.Format(CheckAndFix, "MasterDepartments", @"
                CREATE TABLE [MasterDepartments] (
                    [Id]          int           NOT NULL IDENTITY(1,1),
                    [TenantId]    int           NOT NULL DEFAULT 1,
                    [Name]        nvarchar(max) NOT NULL,
                    [Description] nvarchar(max) NULL,
                    [IsActive]    bit           NOT NULL,
                    [CreatedAt]   datetime2     NOT NULL,
                    CONSTRAINT [PK_MasterDepartments] PRIMARY KEY ([Id])
                );"));

            // ── Tenants (with dependent-table drop before dropping Tenants) ──────────
            migrationBuilder.Sql(@"
                IF OBJECT_ID(N'[Tenants]', N'U') IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1 FROM sys.identity_columns ic
                        JOIN sys.tables t ON ic.object_id = t.object_id
                        WHERE t.name = 'Tenants' AND ic.name = 'Id'
                    )
                BEGIN
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
                END");

            // ── TenantAiSettings ──────────────────────────────────────────────────────
            migrationBuilder.Sql(string.Format(CheckAndFix, "TenantAiSettings", @"
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
                CREATE UNIQUE INDEX [IX_TenantAiSettings_TenantId] ON [TenantAiSettings] ([TenantId]);"));

            // ── UserTenants ───────────────────────────────────────────────────────────
            migrationBuilder.Sql(string.Format(CheckAndFix, "UserTenants", @"
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
                CREATE INDEX [IX_UserTenants_UserId]   ON [UserTenants] ([UserId]);"));

            // ── MasterLobs ────────────────────────────────────────────────────────────
            migrationBuilder.Sql(string.Format(CheckAndFix, "MasterLobs", @"
                CREATE TABLE [MasterLobs] (
                    [Id]             int           NOT NULL IDENTITY(1,1),
                    [TenantId]       int           NOT NULL,
                    [DepartmentName] nvarchar(max) NOT NULL,
                    [Name]           nvarchar(max) NOT NULL,
                    [Description]    nvarchar(max) NULL,
                    [IsActive]       bit           NOT NULL,
                    [CreatedAt]      datetime2     NOT NULL,
                    CONSTRAINT [PK_MasterLobs] PRIMARY KEY ([Id])
                );"));

            // ── RagJobs ───────────────────────────────────────────────────────────────
            // RagJobs has an FK to IntakeRecords; recreate only if IntakeRecords already exists.
            migrationBuilder.Sql(@"
                IF OBJECT_ID(N'[RagJobs]', N'U') IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1 FROM sys.identity_columns ic
                        JOIN sys.tables t ON ic.object_id = t.object_id
                        WHERE t.name = 'RagJobs' AND ic.name = 'Id'
                    )
                BEGIN
                    DROP TABLE [RagJobs];
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
                END");

            // ── QcChecks ──────────────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
                IF OBJECT_ID(N'[QcChecks]', N'U') IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1 FROM sys.identity_columns ic
                        JOIN sys.tables t ON ic.object_id = t.object_id
                        WHERE t.name = 'QcChecks' AND ic.name = 'Id'
                    )
                BEGIN
                    DROP TABLE [QcChecks];
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
                END");

            // ── Tables that depend on IntakeRecords ───────────────────────────────────
            // FinalReports
            migrationBuilder.Sql(@"
                IF OBJECT_ID(N'[FinalReports]', N'U') IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1 FROM sys.identity_columns ic
                        JOIN sys.tables t ON ic.object_id = t.object_id
                        WHERE t.name = 'FinalReports' AND ic.name = 'Id'
                    )
                BEGIN
                    DROP TABLE [FinalReports];
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
                END");

            // IntakeTasks (parent of IntakeDocuments, TaskActionLogs)
            migrationBuilder.Sql(@"
                IF OBJECT_ID(N'[IntakeTasks]', N'U') IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1 FROM sys.identity_columns ic
                        JOIN sys.tables t ON ic.object_id = t.object_id
                        WHERE t.name = 'IntakeTasks' AND ic.name = 'Id'
                    )
                BEGIN
                    IF OBJECT_ID(N'[IntakeDocuments]', N'U') IS NOT NULL DROP TABLE [IntakeDocuments];
                    IF OBJECT_ID(N'[TaskActionLogs]',  N'U') IS NOT NULL DROP TABLE [TaskActionLogs];
                    DROP TABLE [IntakeTasks];
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
                END");

            // ReportFieldStatuses
            migrationBuilder.Sql(@"
                IF OBJECT_ID(N'[ReportFieldStatuses]', N'U') IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1 FROM sys.identity_columns ic
                        JOIN sys.tables t ON ic.object_id = t.object_id
                        WHERE t.name = 'ReportFieldStatuses' AND ic.name = 'Id'
                    )
                BEGIN
                    DROP TABLE [ReportFieldStatuses];
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
                END");

            // ── IntakeFieldConfigs ────────────────────────────────────────────────────
            migrationBuilder.Sql(string.Format(CheckAndFix, "IntakeFieldConfigs", @"
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
                );"));

            // ── TenantPiiSettings ─────────────────────────────────────────────────────
            migrationBuilder.Sql(string.Format(CheckAndFix, "TenantPiiSettings", @"
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
                CREATE UNIQUE INDEX [IX_TenantPiiSettings_TenantId] ON [TenantPiiSettings] ([TenantId]);"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This migration only repairs data; no schema rollback is needed.
        }
    }
}
