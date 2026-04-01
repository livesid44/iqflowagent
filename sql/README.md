# SQL Migration Scripts

Raw SQL Server migration scripts for environments where EF Core migrations cannot be run directly.

## When to use these

Run these scripts if:
- `dotnet ef database update` fails or is not available on your deployment server
- You manage the database separately from the application
- You prefer to review and apply schema changes manually

## Files

| File | Covers |
|------|--------|
| `Migration_20260303122929_AddRagJobsAndSpeech.sql` | Adds `AzureSpeechApiKey`, `AzureSpeechRegion` to `TenantAiSettings`; adds `SopDocumentPath`, `TranscriptStatus`, `TranscriptText` to `IntakeDocuments`; creates the `RagJobs` table |
| `Migration_20260327000000_AddRagSearchSettingsToTenantAiSettings.sql` | Adds `AzureDocumentIntelligenceEndpoint`, `AzureDocumentIntelligenceApiKey`, `AzureOpenAIEmbeddingDeployment`, `AzureSearchEndpoint`, `AzureSearchApiKey`, `AzureSearchIndexName` to `TenantAiSettings` |
| `Migration_20260329000000_AddBartokSectionNameToIntakeTask.sql` | Adds `BartokSectionName` (nullable) to `IntakeTasks` for Bartok document checkpoint task targeting |
| **`Migration_All_RAG_Changes_Combined.sql`** | **Recommended** — applies all three migrations above in the correct order |

## How to run

### SSMS / Azure Data Studio
Open `Migration_All_RAG_Changes_Combined.sql` and execute against your database.

### sqlcmd
```bash
sqlcmd -S <server> -d <database> -U <user> -P <password> \
       -i sql/Migration_All_RAG_Changes_Combined.sql
```

### Azure SQL via az cli
```bash
az sql db execute -s <server> -d <database> \
  --file sql/Migration_All_RAG_Changes_Combined.sql
```

## Safety

All scripts are **idempotent** — they check `COL_LENGTH`, `OBJECT_ID`, and `sys.indexes` before making any changes, so they can be run multiple times safely without duplicating columns or tables.

Each script also inserts the corresponding row in `__EFMigrationsHistory` so that EF Core knows the migration has been applied and won't try to re-run it.
