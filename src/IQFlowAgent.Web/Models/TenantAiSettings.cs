using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace IQFlowAgent.Web.Models;

public class TenantAiSettings
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    [BindNever]
    public Tenant? Tenant { get; set; }
    public string AzureOpenAIEndpoint { get; set; } = string.Empty;
    public string AzureOpenAIApiKey { get; set; } = string.Empty;
    public string AzureOpenAIDeploymentName { get; set; } = string.Empty;
    public string AzureOpenAIApiVersion { get; set; } = "2025-01-01-preview";
    public int AzureOpenAIMaxTokens { get; set; } = 2000;
    public string AzureStorageConnectionString { get; set; } = string.Empty;
    public string AzureStorageContainerName { get; set; } = "intakes";

    // Azure Speech-to-Text (for audio/video transcription)
    public string AzureSpeechRegion { get; set; } = string.Empty;
    public string AzureSpeechApiKey { get; set; } = string.Empty;

    /// <summary>
    /// When true the intake form's Country/City dropdowns are filtered by the selected LOT(s)
    /// using the LotCountryMapping table.  When false, the full global list is shown.
    /// </summary>
    public bool UseCountryFilterByLot { get; set; } = false;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedByUserId { get; set; }
}
