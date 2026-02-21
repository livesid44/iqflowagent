# IQFlowAgent

A .NET 9 ASP.NET Core MVC BPO AI Platform.

## Getting Started

```bash
cd src/IQFlowAgent.Web
dotnet run
```

Default login: **admin** / **Admin@123!** (SuperAdmin)

## Configuration – User Secrets (Development)

Set credentials via .NET User Secrets so real keys are never committed to source control.  
Run these commands from inside the `src/IQFlowAgent.Web` directory:

```bash
cd src/IQFlowAgent.Web

# Azure OpenAI (Azure AI Foundry – cognitiveservices.azure.com)
dotnet user-secrets set "AzureOpenAI:Endpoint"        "https://YOUR_RESOURCE.cognitiveservices.azure.com/"
dotnet user-secrets set "AzureOpenAI:ApiKey"           "YOUR_API_KEY"
dotnet user-secrets set "AzureOpenAI:DeploymentName"   "gpt-4o"
dotnet user-secrets set "AzureOpenAI:ApiVersion"       "2025-01-01-preview"

# Azure Blob Storage (optional – falls back to local disk if not set)
dotnet user-secrets set "AzureStorage:ConnectionString" "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"
dotnet user-secrets set "AzureStorage:ContainerName"    "intakes"
```

> **Tip:** If you see *"Could not find a valid 'UserSecretsId'"* make sure you are running
> the commands from the `src/IQFlowAgent.Web` directory where the `.csproj` lives.

Without Azure OpenAI configured the application runs with mock analysis results.  
Without Azure Blob Storage configured uploaded documents are stored in `wwwroot/uploads/`.

## Roles

| Role       | Permissions                                      |
|------------|--------------------------------------------------|
| SuperAdmin | Full access including Auth Settings              |
| Admin      | User Management, Intake, AI Analysis             |
| User       | Intake, AI Analysis                              |
