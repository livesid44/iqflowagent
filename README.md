# IQFlowAgent

A .NET 9 ASP.NET Core MVC BPO AI Platform.

## Getting Started

```bash
cd src/IQFlowAgent.Web
dotnet run
```

Default login: **admin** / **Admin@123!** (SuperAdmin)

## Azure OpenAI Configuration

Set Azure OpenAI credentials via **user secrets** (development) or **environment variables** / **Azure Key Vault** (production). Do **not** commit real API keys to source control.

```bash
# Development – user secrets
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://YOUR_RESOURCE.openai.azure.com/"
dotnet user-secrets set "AzureOpenAI:ApiKey"   "YOUR_API_KEY"
dotnet user-secrets set "AzureOpenAI:DeploymentName" "gpt-4o"
```

Without Azure OpenAI configured the application runs with mock analysis results.

## Roles

| Role       | Permissions                                      |
|------------|--------------------------------------------------|
| SuperAdmin | Full access including Auth Settings              |
| Admin      | User Management, Intake, AI Analysis             |
| User       | Intake, AI Analysis                              |
