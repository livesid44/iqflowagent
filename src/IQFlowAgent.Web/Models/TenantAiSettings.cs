namespace IQFlowAgent.Web.Models;

public class TenantAiSettings
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public string AzureOpenAIEndpoint { get; set; } = string.Empty;
    public string AzureOpenAIApiKey { get; set; } = string.Empty;
    public string AzureOpenAIDeploymentName { get; set; } = string.Empty;
    public string AzureOpenAIApiVersion { get; set; } = "2025-01-01-preview";
    public int AzureOpenAIMaxTokens { get; set; } = 2000;
    public string AzureStorageConnectionString { get; set; } = string.Empty;
    public string AzureStorageContainerName { get; set; } = "intakes";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedByUserId { get; set; }
}
