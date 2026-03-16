-- =============================================================================
-- IQFlowAgent – UAT Data Cleanup Script (SQL Server)
-- =============================================================================
-- Purpose : Delete all transactional UAT data for IntakeRecords, IntakeTasks,
--           and every child table that hangs off them.
--
-- RETAINS (untouched):
--   • Tenants                  – tenant registry
--   • TenantAiSettings         – AI/Azure configuration per tenant
--   • TenantPiiSettings        – PII detection settings per tenant
--   • AuthSettings             – LDAP / auth config per tenant
--   • UserTenants              – user-to-tenant assignments
--   • MasterDepartments        – department master data
--   • MasterLobs               – line-of-business master data
--   • LotCountryMappings       – LOT-to-country-city lookup (seeded)
--   • IntakeFieldConfigs       – per-tenant form field visibility/order (seeded)
--   • AspNetUsers              – user accounts
--   • AspNetRoles              – roles
--   • AspNetUserRoles          – user-role assignments
--   • AspNetUserClaims         – user claims
--   • AspNetRoleClaims         – role claims
--   • AspNetUserLogins         – external login providers
--   • AspNetUserTokens         – auth tokens
--   • __EFMigrationsHistory    – migration tracking
--
-- DELETES (all rows):
--   1. TaskActionLogs          – audit trail for task status changes
--   2. IntakeDocuments         – uploaded files & transcripts attached to tasks
--   3. ReportFieldStatuses     – BARTOK DD template field tracking
--   4. FinalReports            – generated BARTOK DD report references
--   5. QcChecks                – QC score runs per intake
--   6. RagJobs                 – RAG background jobs per intake
--   7. IntakeTasks             – tasks generated from intake submissions
--   8. IntakeRecords           – root intake submission records
--
-- Deletion order respects FK constraints (children first, parents last).
-- All deletes are wrapped in a single transaction — rolls back on any error.
--
-- Usage (SSMS):
--   Run against the target UAT database after reviewing the pre-flight count.
--
-- Usage (sqlcmd):
--   sqlcmd -S <server> -d <database> -i uat-data-cleanup.sql
--
-- ⚠  WARNING: This script is IRREVERSIBLE. Take a database backup first.
-- ⚠  NOTE: This script does NOT delete physical files from Azure Blob Storage
--           or the local uploads folder. If IntakeDocuments.FilePath or
--           FinalReports.FilePath point to blob storage, delete those blobs
--           separately before or after running this script.
-- =============================================================================

SET NOCOUNT ON;
GO

-- =============================================================================
-- 0. Pre-flight: show current row counts so you can verify before committing
-- =============================================================================
PRINT '=== PRE-FLIGHT ROW COUNTS ===';

SELECT 'TaskActionLogs'     AS TableName, COUNT(*) AS RowCount FROM [TaskActionLogs]
UNION ALL
SELECT 'IntakeDocuments',                  COUNT(*)            FROM [IntakeDocuments]
UNION ALL
SELECT 'ReportFieldStatuses',             COUNT(*)             FROM [ReportFieldStatuses]
UNION ALL
SELECT 'FinalReports',                    COUNT(*)             FROM [FinalReports]
UNION ALL
SELECT 'QcChecks',                        COUNT(*)             FROM [QcChecks]
UNION ALL
SELECT 'RagJobs',                         COUNT(*)             FROM [RagJobs]
UNION ALL
SELECT 'IntakeTasks',                     COUNT(*)             FROM [IntakeTasks]
UNION ALL
SELECT 'IntakeRecords',                   COUNT(*)             FROM [IntakeRecords]
ORDER BY TableName;

PRINT '';
PRINT 'Review the counts above. Press Ctrl+C to abort, or continue to run the cleanup.';
PRINT '';
GO

-- =============================================================================
-- 1. Execute cleanup inside a transaction
-- =============================================================================
BEGIN TRANSACTION;

BEGIN TRY

    -- ------------------------------------------------------------------
    -- 1a. TaskActionLogs
    --     FK: IntakeTaskId → IntakeTasks (cascade in EF, but we delete
    --         explicitly here to remain safe regardless of DB cascade config)
    -- ------------------------------------------------------------------
    DELETE FROM [TaskActionLogs];
    PRINT CONCAT('TaskActionLogs deleted: ', @@ROWCOUNT, ' rows');

    -- ------------------------------------------------------------------
    -- 1b. IntakeDocuments
    --     FK: IntakeRecordId → IntakeRecords (cascade)
    --         IntakeTaskId  → IntakeTasks    (no action — must delete first)
    -- ------------------------------------------------------------------
    DELETE FROM [IntakeDocuments];
    PRINT CONCAT('IntakeDocuments deleted: ', @@ROWCOUNT, ' rows');

    -- ------------------------------------------------------------------
    -- 1c. ReportFieldStatuses
    --     FK: IntakeRecordId → IntakeRecords (cascade)
    -- ------------------------------------------------------------------
    DELETE FROM [ReportFieldStatuses];
    PRINT CONCAT('ReportFieldStatuses deleted: ', @@ROWCOUNT, ' rows');

    -- ------------------------------------------------------------------
    -- 1d. FinalReports
    --     FK: IntakeRecordId → IntakeRecords (cascade)
    -- ------------------------------------------------------------------
    DELETE FROM [FinalReports];
    PRINT CONCAT('FinalReports deleted: ', @@ROWCOUNT, ' rows');

    -- ------------------------------------------------------------------
    -- 1e. QcChecks
    --     FK: IntakeRecordId → IntakeRecords (cascade)
    -- ------------------------------------------------------------------
    DELETE FROM [QcChecks];
    PRINT CONCAT('QcChecks deleted: ', @@ROWCOUNT, ' rows');

    -- ------------------------------------------------------------------
    -- 1f. RagJobs
    --     FK: IntakeRecordId → IntakeRecords (cascade)
    -- ------------------------------------------------------------------
    DELETE FROM [RagJobs];
    PRINT CONCAT('RagJobs deleted: ', @@ROWCOUNT, ' rows');

    -- ------------------------------------------------------------------
    -- 1g. IntakeTasks
    --     FK: IntakeRecordId → IntakeRecords (cascade)
    --     Child tables (TaskActionLogs, IntakeDocuments) already cleared.
    -- ------------------------------------------------------------------
    DELETE FROM [IntakeTasks];
    PRINT CONCAT('IntakeTasks deleted: ', @@ROWCOUNT, ' rows');

    -- ------------------------------------------------------------------
    -- 1h. IntakeRecords  (root table — all children already deleted)
    -- ------------------------------------------------------------------
    DELETE FROM [IntakeRecords];
    PRINT CONCAT('IntakeRecords deleted: ', @@ROWCOUNT, ' rows');

    COMMIT TRANSACTION;
    PRINT '';
    PRINT '✅ UAT data cleanup COMMITTED successfully.';

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    PRINT '';
    PRINT '❌ ERROR — transaction rolled back. No data was deleted.';
    PRINT CONCAT('Error ',    ERROR_NUMBER(), ': ', ERROR_MESSAGE());
    PRINT CONCAT('Severity: ', ERROR_SEVERITY(), '  State: ', ERROR_STATE());
    PRINT CONCAT('Location: Line ', ERROR_LINE(), ' in ', ISNULL(ERROR_PROCEDURE(), 'main script'));
    -- Re-raise so the calling session / pipeline also sees the failure
    THROW;
END CATCH;
GO

-- =============================================================================
-- 2. Reseed identity columns (runs AFTER the transaction commits)
--    This is a separate step so a reseed failure cannot undo the deletions.
--    Optional — comment out if you prefer to keep the current high-water mark.
-- =============================================================================
BEGIN TRY
    DBCC CHECKIDENT ('[TaskActionLogs]',      RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('[IntakeDocuments]',     RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('[ReportFieldStatuses]', RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('[FinalReports]',        RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('[QcChecks]',            RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('[RagJobs]',             RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('[IntakeTasks]',         RESEED, 0) WITH NO_INFOMSGS;
    DBCC CHECKIDENT ('[IntakeRecords]',       RESEED, 0) WITH NO_INFOMSGS;
    PRINT 'Identity columns reseeded to 0 (next insert will use Id = 1).';
END TRY
BEGIN CATCH
    PRINT CONCAT('⚠  Identity reseed warning: ', ERROR_MESSAGE());
    PRINT '    Deletions were already committed — only the reseed was skipped.';
END CATCH;
GO

-- =============================================================================
-- 3. Post-cleanup verification
-- =============================================================================
PRINT '';
PRINT '=== POST-CLEANUP ROW COUNTS (all should be 0) ===';

SELECT 'TaskActionLogs'     AS TableName, COUNT(*) AS RowCount FROM [TaskActionLogs]
UNION ALL
SELECT 'IntakeDocuments',                  COUNT(*)            FROM [IntakeDocuments]
UNION ALL
SELECT 'ReportFieldStatuses',             COUNT(*)             FROM [ReportFieldStatuses]
UNION ALL
SELECT 'FinalReports',                    COUNT(*)             FROM [FinalReports]
UNION ALL
SELECT 'QcChecks',                        COUNT(*)             FROM [QcChecks]
UNION ALL
SELECT 'RagJobs',                         COUNT(*)             FROM [RagJobs]
UNION ALL
SELECT 'IntakeTasks',                     COUNT(*)             FROM [IntakeTasks]
UNION ALL
SELECT 'IntakeRecords',                   COUNT(*)             FROM [IntakeRecords]
ORDER BY TableName;

PRINT '';
PRINT '=== MASTER / CONFIG ROW COUNTS (should be unchanged) ===';

SELECT 'Tenants'            AS TableName, COUNT(*) AS RowCount FROM [Tenants]
UNION ALL
SELECT 'TenantAiSettings',                COUNT(*)             FROM [TenantAiSettings]
UNION ALL
SELECT 'TenantPiiSettings',               COUNT(*)             FROM [TenantPiiSettings]
UNION ALL
SELECT 'AuthSettings',                    COUNT(*)             FROM [AuthSettings]
UNION ALL
SELECT 'UserTenants',                     COUNT(*)             FROM [UserTenants]
UNION ALL
SELECT 'MasterDepartments',               COUNT(*)             FROM [MasterDepartments]
UNION ALL
SELECT 'MasterLobs',                      COUNT(*)             FROM [MasterLobs]
UNION ALL
SELECT 'LotCountryMappings',              COUNT(*)             FROM [LotCountryMappings]
UNION ALL
SELECT 'IntakeFieldConfigs',              COUNT(*)             FROM [IntakeFieldConfigs]
UNION ALL
SELECT 'AspNetUsers',                     COUNT(*)             FROM [AspNetUsers]
UNION ALL
SELECT 'AspNetRoles',                     COUNT(*)             FROM [AspNetRoles]
UNION ALL
SELECT 'AspNetUserRoles',                 COUNT(*)             FROM [AspNetUserRoles]
ORDER BY TableName;

GO
