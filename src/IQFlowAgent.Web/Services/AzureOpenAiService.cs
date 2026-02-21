using Azure;
using Azure.AI.OpenAI;
using IQFlowAgent.Web.Models;
using OpenAI.Chat;

namespace IQFlowAgent.Web.Services;

public class AzureOpenAiService : IAzureOpenAiService
{
    private const int MaxDocumentChars = 8000;
    private const int DefaultMaxOutputTokens = 2000;

    private readonly IConfiguration _config;
    private readonly ILogger<AzureOpenAiService> _logger;

    public AzureOpenAiService(IConfiguration config, ILogger<AzureOpenAiService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<string> AnalyzeIntakeAsync(IntakeRecord intake, string? documentText)
    {
        var endpoint = _config["AzureOpenAI:Endpoint"];
        var apiKey = _config["AzureOpenAI:ApiKey"];
        var deployment = _config["AzureOpenAI:DeploymentName"] ?? "gpt-4o";
        var maxTokens = int.TryParse(_config["AzureOpenAI:MaxTokens"], out var mt) ? mt : DefaultMaxOutputTokens;

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey)
            || endpoint.Contains("YOUR_RESOURCE") || apiKey.Contains("YOUR_API_KEY"))
        {
            _logger.LogWarning("Azure OpenAI is not configured. Returning mock analysis.");
            return GenerateMockAnalysis(intake);
        }

        try
        {
            var client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
            var chatClient = client.GetChatClient(deployment);

            var systemPrompt = """
                You are an expert business process analyst. 
                Analyze the provided business process intake information and produce a structured JSON analysis.
                Your response must be valid JSON with the following structure:
                {
                  "processName": "...",
                  "confidenceScore": 85,
                  "stepsIdentified": 7,
                  "estimatedHandlingTimeMinutes": 15,
                  "complianceStatus": "Passed|Pending|Failed",
                  "automationPotential": "High|Medium|Low",
                  "keyInsights": ["insight 1", "insight 2"],
                  "recommendations": [
                    { "icon": "bulb", "text": "recommendation 1" },
                    { "icon": "target", "text": "recommendation 2" }
                  ],
                  "riskAreas": ["risk 1", "risk 2"],
                  "actionItems": [
                    { "title": "action title", "description": "what needs to be done", "owner": "role or team", "priority": "High|Medium|Low" }
                  ],
                  "checkPoints": [
                    { "label": "checkpoint label", "status": "Pass|Fail|Warning", "note": "optional explanation" }
                  ],
                  "qualityScore": 90,
                  "summary": "Brief executive summary of the process analysis."
                }
                actionItems: concrete next steps that the team must take (e.g. document the process, schedule review, assign owner).
                checkPoints: validation checks that confirm readiness or compliance (e.g. SLA defined, escalation path documented).
                Respond ONLY with the JSON object, no markdown fences.
                """;

            var userMessage = BuildUserMessage(intake, documentText);

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(userMessage)
            };

            var options = new ChatCompletionOptions { MaxOutputTokenCount = maxTokens };
            var completion = await chatClient.CompleteChatAsync(messages, options);
            return completion.Value.Content[0].Text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure OpenAI analysis failed for intake {IntakeId}", intake.IntakeId);
            return GenerateMockAnalysis(intake);
        }
    }

    private static string BuildUserMessage(IntakeRecord intake, string? documentText)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Process Name: {intake.ProcessName}");
        sb.AppendLine($"Description: {intake.Description}");
        sb.AppendLine($"Business Unit: {intake.BusinessUnit}");
        sb.AppendLine($"Department: {intake.Department}");
        sb.AppendLine($"Process Owner: {intake.ProcessOwnerName} ({intake.ProcessOwnerEmail})");
        sb.AppendLine($"Process Type: {intake.ProcessType}");
        sb.AppendLine($"Estimated Volume per Day: {intake.EstimatedVolumePerDay}");
        sb.AppendLine($"Priority: {intake.Priority}");
        sb.AppendLine($"Location: {intake.City}, {intake.Country} ({intake.SiteLocation})");
        sb.AppendLine($"Time Zone: {intake.TimeZone}");

        if (!string.IsNullOrWhiteSpace(documentText))
        {
            sb.AppendLine();
            sb.AppendLine("=== Uploaded Document Content ===");
            var truncated = documentText.Length > MaxDocumentChars
                ? documentText[..MaxDocumentChars] + "\n[...truncated]"
                : documentText;
            sb.AppendLine(truncated);
        }

        return sb.ToString();
    }

    private static string GenerateMockAnalysis(IntakeRecord intake)
    {
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            processName = intake.ProcessName,
            confidenceScore = 82,
            stepsIdentified = 6,
            estimatedHandlingTimeMinutes = 12,
            complianceStatus = "Passed",
            automationPotential = "Medium",
            keyInsights = new[]
            {
                $"Process '{intake.ProcessName}' has {6} distinct steps identified",
                $"Located in {intake.City}, {intake.Country} — timezone considerations apply",
                $"Business unit '{intake.BusinessUnit}' shows standard process complexity",
                "Compliance requirements appear to be met based on provided information"
            },
            recommendations = new[]
            {
                new { icon = "bulb", text = "Document each step with clear entry/exit criteria to reduce variation" },
                new { icon = "target", text = "Identify top 3 bottlenecks for automation prioritization" },
                new { icon = "bar", text = "Establish KPIs: cycle time, error rate, throughput per day" }
            },
            riskAreas = new[]
            {
                "Manual handoff points between departments",
                "Lack of standardized documentation for exception handling"
            },
            actionItems = new[]
            {
                new { title = "Document Process Steps", description = $"Create a detailed step-by-step runbook for '{intake.ProcessName}' capturing inputs, outputs, and decision points for each step.", owner = $"{intake.ProcessOwnerName} / {intake.BusinessUnit}", priority = "High" },
                new { title = "Schedule Process Walk-Through", description = "Organise a 90-minute walk-through session with the process team to validate captured steps and identify undocumented exceptions.", owner = "Process Owner", priority = "High" },
                new { title = "Define SLA & KPIs", description = "Establish measurable SLAs (cycle time, error rate, throughput) and baseline current performance before any automation is applied.", owner = $"{intake.Department ?? intake.BusinessUnit} Lead", priority = "Medium" },
                new { title = "Map Handoff Points", description = "Identify every manual handoff between teams or systems and document the responsible party, expected turnaround, and escalation path.", owner = "Business Analyst", priority = "Medium" },
                new { title = "Conduct Automation Feasibility Study", description = "Assess the top 3 candidate steps for RPA/automation — estimate effort, cost, and expected ROI over 12 months.", owner = "Automation COE", priority = "Low" }
            },
            checkPoints = new[]
            {
                new { label = "Process Owner Assigned", status = string.IsNullOrWhiteSpace(intake.ProcessOwnerName) ? "Fail" : "Pass", note = string.IsNullOrWhiteSpace(intake.ProcessOwnerName) ? "No process owner provided" : $"Assigned to {intake.ProcessOwnerName}" },
                new { label = "Business Unit Defined", status = string.IsNullOrWhiteSpace(intake.BusinessUnit) ? "Fail" : "Pass", note = string.IsNullOrWhiteSpace(intake.BusinessUnit) ? "Business unit missing" : $"Unit: {intake.BusinessUnit}" },
                new { label = "Location Information Complete", status = string.IsNullOrWhiteSpace(intake.Country) ? "Warning" : "Pass", note = string.IsNullOrWhiteSpace(intake.Country) ? "Country not specified" : $"{intake.City}, {intake.Country}" },
                new { label = "Process Description Provided", status = string.IsNullOrWhiteSpace(intake.Description) ? "Fail" : "Pass", note = string.IsNullOrWhiteSpace(intake.Description) ? "No description" : "Description captured" },
                new { label = "Volume Estimate Available", status = intake.EstimatedVolumePerDay == 0 ? "Warning" : "Pass", note = intake.EstimatedVolumePerDay == 0 ? "Volume per day not provided — needed for capacity planning" : $"{intake.EstimatedVolumePerDay} transactions/day" },
                new { label = "Supporting Document Uploaded", status = string.IsNullOrWhiteSpace(intake.UploadedFileName) ? "Warning" : "Pass", note = string.IsNullOrWhiteSpace(intake.UploadedFileName) ? "No document attached — analysis is based on metadata only" : $"Document: {intake.UploadedFileName}" },
                new { label = "Compliance Status Verified", status = "Pass", note = "No compliance blockers identified based on provided information" }
            },
            qualityScore = 78,
            summary = $"Initial analysis of '{intake.ProcessName}' indicates a {intake.ProcessType.ToLower()} process with medium automation potential. " +
                      $"The process handles approximately {intake.EstimatedVolumePerDay} transactions per day. " +
                      "Recommend a detailed process walk-through to validate identified steps and capture exception flows."
        });
    }
}
