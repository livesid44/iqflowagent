# IQFlowAgent

A .NET 9 ASP.NET Core MVC BPO AI Platform.

## Getting Started

```bash
cd src/IQFlowAgent.Web
dotnet run
```

Default login: **admin** / **Admin@123!** (SuperAdmin)

---

## ⚠️ Important: Never edit `appsettings.json` directly

`appsettings.json` is committed to source control with **placeholder values only**.  
Editing it directly to add real credentials will cause a `System.IO.InvalidDataException` on startup
if you accidentally introduce a JSON syntax error (e.g. an unescaped backslash in a connection string).

**Always configure credentials using one of the two safe methods below.**

---

## Configuration – Option A: appsettings.Development.json (Recommended for local dev)

`appsettings.Development.json` is in `.gitignore` — it will never be committed.  
Copy the example and fill in your values:

```bash
cd src/IQFlowAgent.Web
# The file already exists with empty values — just open it and fill in your credentials:
# appsettings.Development.json
```

The file contains empty strings for all keys. Simply replace the empty strings:

```json
{
  "ConnectionStrings": {
    "SqlServer": "Server=YOUR_SERVER;Database=IQFlowAgent;User Id=sa;Password=YOUR_PASS;TrustServerCertificate=True;"
  },
  "AzureOpenAI": {
    "Endpoint": "https://YOUR_RESOURCE.cognitiveservices.azure.com/",
    "ApiKey": "YOUR_API_KEY",
    "DeploymentName": "gpt-4o",
    "ApiVersion": "2025-01-01-preview"
  },
  "AzureStorage": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net",
    "ContainerName": "intakes"
  }
}
```

See `appsettings.Local.json.example` for a complete reference with example values.

---

## Configuration – Option B: dotnet user-secrets

Run these commands from inside the `src/IQFlowAgent.Web` directory:

```bash
cd src/IQFlowAgent.Web

# Azure OpenAI (Azure AI Foundry – cognitiveservices.azure.com)
dotnet user-secrets set "AzureOpenAI:Endpoint"        "https://YOUR_RESOURCE.cognitiveservices.azure.com/"
dotnet user-secrets set "AzureOpenAI:ApiKey"           "YOUR_API_KEY"
dotnet user-secrets set "AzureOpenAI:DeploymentName"   "gpt-4o"
dotnet user-secrets set "AzureOpenAI:ApiVersion"       "2025-01-01-preview"

# SQL Server (optional – falls back to SQLite if not set)
dotnet user-secrets set "ConnectionStrings:SqlServer"  "Server=YOUR_SERVER;Database=IQFlowAgent;User Id=sa;Password=YOUR_PASS;TrustServerCertificate=True;"

# Azure Blob Storage (optional – falls back to local disk if not set)
dotnet user-secrets set "AzureStorage:ConnectionString" "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"
dotnet user-secrets set "AzureStorage:ContainerName"    "intakes"
```

> **Tip:** If you see *"Could not find a valid 'UserSecretsId'"* make sure you are running
> the commands from the `src/IQFlowAgent.Web` directory where the `.csproj` lives.

---

## Fallback behaviour

| Service | When NOT configured | Behaviour |
|---------|---------------------|-----------|
| Azure OpenAI | Empty `ApiKey` or `DeploymentName` | Mock analysis results (all features work) |
| Azure Blob Storage | Empty `ConnectionString` | Files stored in `wwwroot/uploads/` |
| SQL Server | Empty `SqlServer` connection string | SQLite (`iqflowagent.db` auto-created) |

## Troubleshooting Build Errors

### `CS1061: 'DbContextOptionsBuilder' does not contain a definition for 'UseSqlServer'` or `UseSqlite`  
### `CS0246: The type or namespace name 'DocumentFormat' could not be found`

These errors mean NuGet packages have not been restored on your machine.

**Fix – run one of these from the repo root:**

```bash
# Option 1 – command line (recommended)
dotnet restore src/IQFlowAgent.Web/IQFlowAgent.Web.csproj

# Option 2 – Visual Studio
# Right-click the Solution in Solution Explorer → "Restore NuGet Packages"
```

If restore fails due to a package-source issue, a `NuGet.Config` is now included at the repository root that explicitly points to `https://api.nuget.org/v3/index.json`.

> **Tip for corporate environments:** If nuget.org is blocked by a proxy, ask your IT team for the internal NuGet feed URL and add it to your local `%AppData%\NuGet\NuGet.Config`.

---

## Roles

| Role       | Permissions                                      |
|------------|--------------------------------------------------|
| SuperAdmin | Full access including Auth Settings              |
| Admin      | User Management, Intake, AI Analysis             |
| User       | Intake, AI Analysis                              |
