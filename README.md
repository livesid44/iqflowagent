# IQFlowAgent

A .NET 8 ASP.NET Core MVC BPO AI Platform.

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

---

## Recommended Azure OpenAI Models

The following models are recommended for BARTOK SOP / Due Diligence analysis.
Create a deployment in [Azure AI Foundry](https://ai.azure.com) and set the **Deployment Name** in
`appsettings.Development.json` (or in the AI Settings page inside the app).

| Model | Recommendation | Why |
|-------|----------------|-----|
| **gpt-4o** ⭐ | **Best choice** | 128 k-token context window — handles large uploaded Word/Excel/PDF documents in a single pass. Best structured-output quality for RACI matrices, SOP tables and escalation blocks. |
| gpt-4o-mini | Cost-efficient alternative | 3–4× cheaper per token; suitable for smaller intakes or high-volume processing where budget matters. Quality is slightly lower. |
| o3-mini | Advanced reasoning | Strong at multi-step compliance/regulatory logic. Slower than gpt-4o; use only when complex reasoning is the bottleneck. |

> **API Version:** Use `2025-01-01-preview` or later. Both `gpt-4o` and `o3-mini` require a
> preview API version for full structured-output support.

---



### Recommended: use `publish.cmd` (avoids all VS Publish issues)

```bat
REM From the repo root:
publish.cmd
```

Output lands in `src\IQFlowAgent.Web\bin\publish\`.  
Copy the contents to your IIS / App Service `wwwroot`.

### VS Publish (advanced)

1. Right-click the project → **Publish**
2. Click **Import Profile** → select `src/IQFlowAgent.Web/Properties/PublishProfiles/FolderPublish.pubxml`
3. Click **Publish**

> ⚠️ **Do NOT use a publish profile created by VS before the .NET 8 downgrade.**  
> Old profiles have `<TargetFramework>net9.0</TargetFramework>` which causes publish to fail  
> with *"The current .NET SDK does not support targeting .NET 9.0"*.  
> **Fix:** delete all `.pubxml` files in `Properties/PublishProfiles/` except `FolderPublish.pubxml`,  
> then import `FolderPublish.pubxml` as described above.

---

## Troubleshooting Build Errors

### `The current .NET SDK does not support targeting .NET 9.0` (during VS Publish)

**Cause:** You have a locally-created VS publish profile (created before the project was  
downgraded from .NET 9 to .NET 8) that still contains `<TargetFramework>net9.0</TargetFramework>`.  
VS Publish uses that old profile instead of the committed `FolderPublish.pubxml`.

**Fix — two options:**

**Option 1 (recommended): Use `publish.cmd`**
```bat
publish.cmd
```

**Option 2: Fix the VS Publish profile**
1. In Solution Explorer → expand `Properties/PublishProfiles/`
2. Delete any `.pubxml` file that is NOT named `FolderPublish.pubxml`
3. Right-click project → Publish → **Import Profile** → select `FolderPublish.pubxml`
4. Publish again

A `Directory.Build.targets` file is also included as a safety net — it silently corrects  
`TargetFramework=net9.0` back to `net8.0` during any MSBuild invocation. After pulling  
the latest code, run:
```powershell
cd src\IQFlowAgent.Web; Remove-Item -Recurse -Force bin,obj -ErrorAction SilentlyContinue; dotnet restore; dotnet build
```

---

### `NETSDK1005: Assets file doesn't have a target for 'net8.0'`

This error means `obj/project.assets.json` was generated when the project targeted a **different framework** (e.g. `net9.0`). Even though the project now targets `net8.0`, the stale assets file is used instead of being regenerated.

**Fix — run these commands from `src/IQFlowAgent.Web`:**

```bat
rmdir /s /q bin obj
dotnet restore
dotnet build
```

**PowerShell one-liner:**
```powershell
cd src\IQFlowAgent.Web; Remove-Item -Recurse -Force bin,obj -ErrorAction SilentlyContinue; dotnet restore; dotnet build
```

**Alternative fix in Visual Studio:**
1. Close Visual Studio
2. Delete `src/IQFlowAgent.Web/bin/` and `src/IQFlowAgent.Web/obj/` in Windows Explorer
3. Re-open the solution → Build

---

### `MSB4018: GenerateStaticWebAssetsPropsFile task failed – DirectoryNotFoundException`

**Full error text:**
```
MSB4018 The "GenerateStaticWebAssetsPropsFile" task failed unexpectedly.
System.IO.DirectoryNotFoundException: Could not find a part of the path '...\obj\Debug\net8.0\staticwebassets\msbuild.IQFlowAgent.Web.Microsoft.AspNetCore.StaticWebAssets.props'
```

**Cause:** Your checkout folder path is too long. Windows has a **MAX_PATH limit of 260 characters**.  
Deeply-nested folder names like `iqflowagent-copilot-start-dotnet-application-login-user-management (4)\new\iqflowagent-copilot-...` push the total path length past 260 chars, and old MSBuild I/O code cannot create the `obj\` sub-directories.

**Fix — three options (pick any one):**

**Option 1 — Enable Windows long-path support (recommended, permanent fix):**
```powershell
# Run PowerShell as Administrator:
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem" `
    -Name "LongPathsEnabled" -Value 1 -Type DWord
# Then restart Visual Studio / your terminal.
```
Or via Group Policy: `Computer Configuration → Administrative Templates → System → Filesystem → Enable Win32 long paths → Enabled`

**Option 2 — Use the `IQFLOW_SHORT_PATHS` env var (no admin rights required):**

A `Directory.Build.props` is included that redirects `obj/` and `bin/` to `%TEMP%\iqflow\` (a short path) when this variable is set to `1`.

```bat
REM In cmd before building:
set IQFLOW_SHORT_PATHS=1
dotnet build
```
```powershell
# In PowerShell before building:
$env:IQFLOW_SHORT_PATHS="1"
dotnet build
```
For a permanent fix without admin rights: add `IQFLOW_SHORT_PATHS=1` in  
*System Properties → Advanced → Environment Variables → User variables*,  
then restart VS.

**Option 3 — Clone to a shorter path (simplest):**
```bat
cd C:\dev
git clone https://github.com/livesid44/iqflowagent iq
cd iq
dotnet build src\IQFlowAgent.Web\IQFlowAgent.Web.csproj
```

---

### `CS1061: 'DbContextOptionsBuilder' does not contain a definition for 'UseSqlServer'` or `UseSqlite`  
### `CS0246: The type or namespace name 'DocumentFormat' could not be found`

These errors mean Visual Studio's cached type information (`obj/project.assets.json`) is stale and does not yet know about the newly added packages. This happens even when NuGet says **"All packages are already installed"** — the packages are in the cache but the assets file has not been regenerated.

**Definitive fix — same as NETSDK1005 above:**

```bat
cd src\IQFlowAgent.Web
rmdir /s /q bin obj
dotnet restore
dotnet build
```

**Alternative fix in Visual Studio:**
1. Close Visual Studio  
2. Delete the `src/IQFlowAgent.Web/bin/` and `src/IQFlowAgent.Web/obj/` folders in Explorer  
3. Re-open the solution and build

If restore fails due to a package-source issue, a `NuGet.Config` is included at the repository root that explicitly points to `https://api.nuget.org/v3/index.json`.

> **Tip for corporate environments:** If nuget.org is blocked by a proxy, ask your IT team for the internal NuGet feed URL and add it to your local `%AppData%\NuGet\NuGet.Config`.

---

## Roles

| Role       | Permissions                                      |
|------------|--------------------------------------------------|
| SuperAdmin | Full access including Auth Settings              |
| Admin      | User Management, Intake, AI Analysis             |
| User       | Intake, AI Analysis                              |
