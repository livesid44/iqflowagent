using IQFlowAgent.Web.Models;
using System.Net.Http.Headers;
using System.Text.Json;

namespace IQFlowAgent.Web.Services;

public class AzureOpenAiService : IAzureOpenAiService
{
    private const int MaxDocumentChars = 8000;
    private const int DefaultMaxOutputTokens = 2000;
    private const int MaxAnalysisJsonChars = 3000;  // max chars from AI analysis sent in field-analysis prompt
    private const int MaxArtifactCharsPerReport = 8000;  // aggregate artifact chars for report field analysis

    private readonly IConfiguration _config;
    private readonly ILogger<AzureOpenAiService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITenantContextService _tenantContext;

    public AzureOpenAiService(IConfiguration config, ILogger<AzureOpenAiService> logger,
        IHttpClientFactory httpClientFactory, ITenantContextService tenantContext)
    {
        _config = config;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _tenantContext = tenantContext;
    }

    private async Task<(string? endpoint, string? apiKey, string? deployment, string apiVersion, int maxTokens)> GetAiConfigAsync()
    {
        var tenantSettings = await _tenantContext.GetCurrentTenantAiSettingsAsync();
        if (tenantSettings != null
            && !string.IsNullOrWhiteSpace(tenantSettings.AzureOpenAIEndpoint)
            && !string.IsNullOrWhiteSpace(tenantSettings.AzureOpenAIApiKey)
            && !string.IsNullOrWhiteSpace(tenantSettings.AzureOpenAIDeploymentName)
            && !tenantSettings.AzureOpenAIEndpoint.Contains("YOUR_RESOURCE")
            && !tenantSettings.AzureOpenAIApiKey.Contains("YOUR_API_KEY")
            && !tenantSettings.AzureOpenAIDeploymentName.Equals("YOUR_DEPLOYMENT_NAME", StringComparison.OrdinalIgnoreCase))
        {
            return (tenantSettings.AzureOpenAIEndpoint, tenantSettings.AzureOpenAIApiKey,
                tenantSettings.AzureOpenAIDeploymentName, tenantSettings.AzureOpenAIApiVersion,
                tenantSettings.AzureOpenAIMaxTokens);
        }
        var endpoint = _config["AzureOpenAI:Endpoint"];
        var apiKey = _config["AzureOpenAI:ApiKey"];
        var deployment = _config["AzureOpenAI:DeploymentName"];
        var apiVersion = _config["AzureOpenAI:ApiVersion"] ?? "2025-01-01-preview";
        var maxTokens = int.TryParse(_config["AzureOpenAI:MaxTokens"], out var mt) ? mt : DefaultMaxOutputTokens;
        return (endpoint, apiKey, deployment, apiVersion, maxTokens);
    }

    public async Task<string> AnalyzeIntakeAsync(IntakeRecord intake, string? documentText)
    {
        var (endpoint, apiKey, deployment, apiVersion, maxTokens) = await GetAiConfigAsync();

        // Guard: all three fields must be set to real values
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(deployment)
            || endpoint.Contains("YOUR_RESOURCE") || apiKey.Contains("YOUR_API_KEY")
            || deployment.Equals("YOUR_DEPLOYMENT_NAME", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Azure OpenAI is not fully configured. " +
                "Set 'AzureOpenAI:Endpoint', 'AzureOpenAI:ApiKey', and 'AzureOpenAI:DeploymentName' " +
                "via user secrets or environment variables. Returning mock analysis.");
            return GenerateMockAnalysis(intake);
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
        {
            _logger.LogWarning(
                "Azure OpenAI endpoint '{Endpoint}' is not a valid URI. " +
                "Expected format: https://YOUR_RESOURCE.cognitiveservices.azure.com/. Returning mock analysis.",
                endpoint);
            return GenerateMockAnalysis(intake);
        }

        // Build the URL exactly as shown in the Azure AI Foundry cURL example:
        // POST {endpoint}/openai/deployments/{deployment}/chat/completions?api-version={version}
        var requestUrl = $"{endpoint.TrimEnd('/')}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";

        _logger.LogInformation(
            "Calling Azure OpenAI — url: {RequestUrl}", requestUrl);

        try
        {
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

            var requestBody = new
            {
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = BuildUserMessage(intake, documentText) }
                },
                max_tokens  = maxTokens,
                temperature = 0.7,
                top_p       = 1.0,
                model       = deployment   // included for compatibility; Azure ignores it
            };

            var http = _httpClientFactory.CreateClient();
            // Azure AI Foundry uses "Authorization: Bearer <key>" — matches the cURL in the portal
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);

            var response = await http.PostAsJsonAsync(requestUrl, requestBody);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                if ((int)response.StatusCode == 404)
                {
                    _logger.LogWarning(
                        "Azure OpenAI returned 404 for intake {IntakeId}. " +
                        "Deployment '{Deployment}' was not found at {RequestUrl}. " +
                        "Check the deployment name in Azure AI Foundry → Deployments (case-sensitive). " +
                        "Response: {ErrorBody}. Falling back to mock analysis.",
                        intake.IntakeId, deployment, requestUrl, errorBody);
                }
                else
                {
                    _logger.LogError(
                        "Azure OpenAI returned HTTP {StatusCode} for intake {IntakeId}. " +
                        "Response: {ErrorBody}. Falling back to mock analysis.",
                        (int)response.StatusCode, intake.IntakeId, errorBody);
                }
                return GenerateMockAnalysis(intake);
            }

            var responseText = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseText);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
                return GenerateMockAnalysis(intake);

            return InjectSourceField(content, "llm");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure OpenAI analysis failed for intake {IntakeId}", intake.IntakeId);
            return GenerateMockAnalysis(intake);
        }
    }

    // ── Verify Intake Closure ────────────────────────────────────────────────
    public async Task<string> VerifyIntakeClosureAsync(
        IntakeRecord intake, IList<IntakeTask> tasks, string? aggregatedArtifactText)
    {
        var (endpoint, apiKey, deployment, apiVersion, maxTokens) = await GetAiConfigAsync();

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(deployment)
            || endpoint.Contains("YOUR_RESOURCE") || apiKey.Contains("YOUR_API_KEY")
            || deployment.Equals("YOUR_DEPLOYMENT_NAME", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Azure OpenAI not configured — returning mock closure verification for intake {IntakeId}.",
                intake.IntakeId);
            return GenerateMockVerification(intake, tasks);
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
        {
            _logger.LogWarning("Azure OpenAI endpoint '{Endpoint}' is invalid. Returning mock closure verification.", endpoint);
            return GenerateMockVerification(intake, tasks);
        }

        var requestUrl = $"{endpoint.TrimEnd('/')}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";
        _logger.LogInformation("Calling Azure OpenAI for closure verification — url: {RequestUrl}", requestUrl);

        try
        {
            var systemPrompt = """
                You are an expert business process quality auditor.
                You will be given information about a business process intake and its associated tasks.
                Your job is to verify whether each task has sufficient closure evidence to be considered truly completed,
                or whether it should be reopened for additional work.

                For each task examine:
                - The task title and description (what was required)
                - The comments / action log (what was actually done)
                - The artifact file names (what documents were produced)

                Respond ONLY with a valid JSON object matching exactly this structure (no markdown fences):
                {
                  "canCloseIntake": true,
                  "summary": "Overall verification summary...",
                  "taskVerifications": [
                    {
                      "taskId": "TSK-...",
                      "title": "...",
                      "canClose": true,
                      "reason": "Explanation of verdict",
                      "missingEvidence": []
                    }
                  ]
                }
                canCloseIntake must be true ONLY if every task's canClose is true.
                missingEvidence is an array of strings describing what specific evidence is absent (empty array if canClose is true).
                """;

            var requestBody = new
            {
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = BuildVerificationMessage(intake, tasks, aggregatedArtifactText) }
                },
                max_tokens  = maxTokens,
                temperature = 0.3,
                top_p       = 1.0,
                model       = deployment
            };

            var http = _httpClientFactory.CreateClient();
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);

            var response = await http.PostAsJsonAsync(requestUrl, requestBody);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError(
                    "Azure OpenAI returned HTTP {StatusCode} for closure verification of intake {IntakeId}. " +
                    "Response: {ErrorBody}. Falling back to mock.",
                    (int)response.StatusCode, intake.IntakeId, errorBody);
                return GenerateMockVerification(intake, tasks);
            }

            var responseText = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseText);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
                return GenerateMockVerification(intake, tasks);

            return InjectSourceField(content, "llm");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure OpenAI closure verification failed for intake {IntakeId}", intake.IntakeId);
            return GenerateMockVerification(intake, tasks);
        }
    }

    private static string BuildVerificationMessage(
        IntakeRecord intake, IList<IntakeTask> tasks, string? aggregatedArtifactText)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"INTAKE: {intake.IntakeId}");
        sb.AppendLine($"Process: {intake.ProcessName}");
        sb.AppendLine($"Description: {intake.Description}");
        sb.AppendLine();
        sb.AppendLine("TASKS TO VERIFY:");
        foreach (var t in tasks)
        {
            sb.AppendLine($"---");
            sb.AppendLine($"TaskId: {t.TaskId}");
            sb.AppendLine($"Title: {t.Title}");
            sb.AppendLine($"Description: {t.Description}");
            sb.AppendLine($"Priority: {t.Priority}");
            sb.AppendLine($"Status: {t.Status}");

            var comments = t.ActionLogs
                .Where(l => l.ActionType == "Comment" && !string.IsNullOrWhiteSpace(l.Comment))
                .OrderBy(l => l.CreatedAt)
                .ToList();
            if (comments.Count > 0)
            {
                sb.AppendLine("Comments:");
                foreach (var c in comments)
                    sb.AppendLine($"  - [{c.CreatedAt:yyyy-MM-dd HH:mm}] {c.Comment}");
            }
            else
            {
                sb.AppendLine("Comments: (none)");
            }

            var artifacts = t.Documents.Where(d => d.DocumentType == "TaskArtifact").ToList();
            if (artifacts.Count > 0)
            {
                sb.AppendLine("Artifacts uploaded:");
                foreach (var a in artifacts)
                    sb.AppendLine($"  - {a.FileName}");
            }
            else
            {
                sb.AppendLine("Artifacts: (none uploaded)");
            }
        }

        if (!string.IsNullOrWhiteSpace(aggregatedArtifactText))
        {
            sb.AppendLine();
            sb.AppendLine("=== ARTIFACT CONTENT EXCERPTS ===");
            var truncated = aggregatedArtifactText.Length > MaxDocumentChars
                ? aggregatedArtifactText[..MaxDocumentChars] + "\n[...truncated]"
                : aggregatedArtifactText;
            sb.AppendLine(truncated);
        }

        return sb.ToString();
    }

    private static string GenerateMockVerification(IntakeRecord intake, IList<IntakeTask> tasks)
    {
        var taskVerifications = tasks.Select(t =>
        {
            // Tasks with no action log comments AND no artifacts are flagged for missing evidence
            var hasComments  = t.ActionLogs.Any(l => l.ActionType == "Comment" && !string.IsNullOrWhiteSpace(l.Comment));
            var hasArtifacts = t.Documents.Any(d => d.DocumentType == "TaskArtifact");
            var canClose     = t.Status == "Completed" && (hasComments || hasArtifacts);
            var missing      = new List<string>();
            if (!hasComments && !hasArtifacts)
            {
                missing.Add("No comments or artifacts provided as closure evidence");
                missing.Add("Add a comment describing what was done, or upload a completion artifact");
            }
            return new
            {
                taskId   = t.TaskId,
                title    = t.Title,
                canClose,
                reason   = canClose
                    ? "Task has sufficient closure evidence (comments or artifacts present)."
                    : (t.Status == "Cancelled"
                        ? "Task was cancelled — accepted as closure."
                        : "Task is marked complete but lacks closure evidence (no comments or artifacts)."),
                missingEvidence = missing
            };
        }).ToList();

        // Cancelled tasks always pass
        var verifications = tasks.Zip(taskVerifications, (t, v) =>
        {
            if (t.Status == "Cancelled")
                return new { taskId = t.TaskId, title = t.Title, canClose = true,
                             reason = "Task was cancelled — accepted as closed.",
                             missingEvidence = new List<string>() };
            return new { taskId = v.taskId, title = v.title, canClose = v.canClose,
                         reason = v.reason, missingEvidence = v.missingEvidence };
        }).ToList();

        var allPass = verifications.All(v => v.canClose);
        return JsonSerializer.Serialize(new
        {
            canCloseIntake     = allPass,
            summary            = allPass
                ? $"All {tasks.Count} tasks for intake {intake.IntakeId} have sufficient closure evidence. Intake can be closed."
                : $"{verifications.Count(v => !v.canClose)} of {tasks.Count} tasks lack closure evidence and have been reopened. Please address the missing items before closing.",
            taskVerifications  = verifications
        });
    }

    // ── Analyze Report Fields ────────────────────────────────────────────────
    public async Task<string> AnalyzeReportFieldsAsync(
        IntakeRecord intake,
        string fieldDefinitionsJson,
        string? analysisJson,
        string? artifactText)
    {
        var (endpoint, apiKey, deployment, apiVersion, maxTokens) = await GetAiConfigAsync();

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(deployment)
            || endpoint.Contains("YOUR_RESOURCE") || apiKey.Contains("YOUR_API_KEY")
            || deployment.Equals("YOUR_DEPLOYMENT_NAME", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Azure OpenAI not configured — returning mock report field analysis for intake {IntakeId}.",
                intake.IntakeId);
            return GenerateMockFieldAnalysis(intake, fieldDefinitionsJson, analysisJson);
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
        {
            _logger.LogWarning("Azure OpenAI endpoint is invalid. Returning mock report field analysis.");
            return GenerateMockFieldAnalysis(intake, fieldDefinitionsJson, analysisJson);
        }

        var requestUrl = $"{endpoint.TrimEnd('/')}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";
        _logger.LogInformation("Calling Azure OpenAI for report field analysis — url: {RequestUrl}", requestUrl);

        var systemPrompt = """
            You are an expert business process documentation specialist.
            You will be given:
            1. Information about a business process intake
            2. An AI analysis of that process (JSON)
            3. A list of template fields from the BARTOK Due Diligence document
            4. Optionally, text excerpts from task artifacts

            For each template field, determine:
            - Whether the field can be filled from the available information (status: "Available" or "Missing")
            - The exact value to use if Available (fillValue)
            - A brief note explaining your determination (notes)

            Respond ONLY with valid JSON matching this structure (no markdown fences):
            {
              "fields": [
                {
                  "key": "field_key",
                  "status": "Available",
                  "fillValue": "the value to insert",
                  "notes": "Source: intake process name"
                }
              ]
            }

            Be specific and use actual data from the intake/analysis. For narrative fields, write complete professional sentences.
            For "Missing" fields, explain concisely what additional information is needed.
            """;

        var userMessage = BuildFieldAnalysisMessage(intake, fieldDefinitionsJson, analysisJson, artifactText);

        var requestBody = new
        {
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userMessage }
            },
            max_tokens  = maxTokens,
            temperature = 0.3,
            top_p       = 1.0,
            model       = deployment
        };

        try
        {
            var http = _httpClientFactory.CreateClient();
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var response = await http.PostAsJsonAsync(requestUrl, requestBody);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError(
                    "Azure OpenAI returned HTTP {StatusCode} for report field analysis of intake {IntakeId}. " +
                    "Response: {ErrorBody}. Falling back to mock.",
                    (int)response.StatusCode, intake.IntakeId, errorBody);
                return GenerateMockFieldAnalysis(intake, fieldDefinitionsJson, analysisJson);
            }

            var responseText = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseText);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return string.IsNullOrWhiteSpace(content)
                ? GenerateMockFieldAnalysis(intake, fieldDefinitionsJson, analysisJson)
                : content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure OpenAI report field analysis failed for intake {IntakeId}", intake.IntakeId);
            return GenerateMockFieldAnalysis(intake, fieldDefinitionsJson, analysisJson);
        }
    }

    private static string BuildFieldAnalysisMessage(
        IntakeRecord intake, string fieldDefinitionsJson,
        string? analysisJson, string? artifactText)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== INTAKE INFORMATION ===");
        sb.AppendLine($"Intake ID: {intake.IntakeId}");
        sb.AppendLine($"Process Name: {intake.ProcessName}");
        sb.AppendLine($"Description: {intake.Description}");
        sb.AppendLine($"Business Unit: {intake.BusinessUnit}");
        sb.AppendLine($"Department: {intake.Department}");
        sb.AppendLine($"Process Owner: {intake.ProcessOwnerName} ({intake.ProcessOwnerEmail})");
        sb.AppendLine($"Process Type: {intake.ProcessType}");
        sb.AppendLine($"Volume/Day: {intake.EstimatedVolumePerDay}");
        sb.AppendLine($"Location: {intake.City}, {intake.Country} ({intake.SiteLocation})");
        sb.AppendLine($"Time Zone: {intake.TimeZone}");
        sb.AppendLine($"Uploaded Document: {intake.UploadedFileName ?? "(none)"}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(analysisJson))
        {
            sb.AppendLine("=== AI ANALYSIS RESULT ===");
            var truncated = analysisJson.Length > MaxAnalysisJsonChars
                ? analysisJson[..MaxAnalysisJsonChars] + "\n[...truncated]"
                : analysisJson;
            sb.AppendLine(truncated);
            sb.AppendLine();
        }

        sb.AppendLine("=== TEMPLATE FIELDS TO ANALYZE ===");
        sb.AppendLine(fieldDefinitionsJson);

        if (!string.IsNullOrWhiteSpace(artifactText))
        {
            sb.AppendLine();
            sb.AppendLine("=== TASK ARTIFACT TEXT EXCERPTS ===");
            var truncated = artifactText.Length > MaxArtifactCharsPerReport
                ? artifactText[..MaxArtifactCharsPerReport] + "\n[...truncated]"
                : artifactText;
            sb.AppendLine(truncated);
        }

        return sb.ToString();
    }

    private static string GenerateMockFieldAnalysis(
        IntakeRecord intake, string fieldDefinitionsJson, string? analysisJson)
    {
        // Parse field keys from the definitions JSON
        var fields = new List<object>();
        try
        {
            using var doc = JsonDocument.Parse(fieldDefinitionsJson);
            foreach (var fd in doc.RootElement.EnumerateArray())
            {
                var key    = fd.TryGetProperty("key",   out var k) ? k.GetString() ?? "" : "";
                var label  = fd.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
                var source = fd.TryGetProperty("autoSource", out var s) ? s.GetString() ?? "" : "";

                // Resolve value from intake or analysis JSON
                var (status, fillValue, notes) = ResolveMockField(key, label, source, intake, analysisJson);
                fields.Add(new { key, status, fillValue, notes });
            }
        }
        catch
        {
            // If parse fails, return a minimal valid response
        }

        return JsonSerializer.Serialize(new { fields });
    }

    private static (string status, string fillValue, string notes) ResolveMockField(
        string key, string label, string source, IntakeRecord intake, string? analysisJson)
    {
        // Auto-resolve from intake properties
        var intakeValue = source switch
        {
            "ProcessName"     => intake.ProcessName,
            "BusinessUnit"    => intake.BusinessUnit,
            "Department"      => intake.Department,
            "ProcessOwnerName" => intake.ProcessOwnerName,
            "ProcessType"     => intake.ProcessType,
            "TimeZone"        => intake.TimeZone,
            "Description"     => intake.Description,
            "UploadedFileName" => intake.UploadedFileName ?? "",
            "GeoLocation"     => FormatGeoLocation(intake),
            "TODAY"           => DateTime.UtcNow.ToString("dd MMM yyyy"),
            _                 => null
        };

        if (intakeValue != null)
        {
            return string.IsNullOrWhiteSpace(intakeValue)
                ? ("Missing", "", $"'{label}' could not be resolved from intake — value is empty.")
                : ("Available", intakeValue, $"Auto-resolved from intake: {source}");
        }

        // Resolve from AI analysis JSON
        if (source.StartsWith("AI:", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(analysisJson))
        {
            var aiProp = source["AI:".Length..];
            try
            {
                using var doc = JsonDocument.Parse(analysisJson);
                var root = doc.RootElement;
                if (aiProp == "summary" && root.TryGetProperty("summary", out var sum))
                    return ("Available", sum.GetString() ?? "", "Sourced from AI process analysis summary.");
                if (aiProp == "confidenceScore" && root.TryGetProperty("confidenceScore", out var cs))
                    return ("Available", cs.GetRawText(), "Sourced from AI confidence score.");
                if (aiProp == "flowSummary" && root.TryGetProperty("summary", out var fs))
                    return ("Available",
                        $"{fs.GetString()} Process type: {intake.ProcessType}. " +
                        $"Volume: {intake.EstimatedVolumePerDay} transactions/day.",
                        "Sourced from AI analysis summary.");
                if (aiProp == "operatingModel")
                    return ("Available",
                        $"The process '{intake.ProcessName}' is operated by the {intake.BusinessUnit} " +
                        $"team in {FormatGeoLocation(intake)} under {intake.ProcessOwnerName}. " +
                        $"The team follows a {intake.ProcessType} model with an estimated volume of " +
                        $"{intake.EstimatedVolumePerDay} transactions per day.",
                        "Auto-generated from intake metadata.");
            }
            catch { /* ignore */ }
        }

        // Fields with no source — mark as Missing
        return ("Missing", "", $"No automatic source available for '{label}'. Please provide this information manually or create a task to gather it.");
    }

    // ── End of AnalyzeReportFieldsAsync ─────────────────────────────────────

    /// <summary>Formats city, country and optional site into a single location string.</summary>
    private static string FormatGeoLocation(IntakeRecord intake) =>
        $"{intake.City}, {intake.Country}" +
        (string.IsNullOrWhiteSpace(intake.SiteLocation) ? "" : $" ({intake.SiteLocation})");

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
            _source = "mock",
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

    // ── RunQcCheckAsync ───────────────────────────────────────────────────────

    public async Task<string> RunQcCheckAsync(
        IntakeRecord intake, string? analysisJson, string? tasksSummary, string? documentText)
    {
        var (endpoint, apiKey, deployment, apiVersion, maxTokens) = await GetAiConfigAsync();

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(deployment)
            || endpoint.Contains("YOUR_RESOURCE") || apiKey.Contains("YOUR_API_KEY")
            || deployment.Equals("YOUR_DEPLOYMENT_NAME", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Azure OpenAI not configured — returning mock QC result for {IntakeId}.", intake.IntakeId);
            return GenerateMockQcResult(intake);
        }

        var requestUrl = $"{endpoint.TrimEnd('/')}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";
        _logger.LogInformation("Running QC check for {IntakeId} via {Url}", intake.IntakeId, requestUrl);

        const string systemPrompt = """
            You are a quality assurance expert for business process automation.
            Evaluate the provided intake record, its AI analysis, task completion evidence, and any uploaded documents.
            Score the intake on each of the following QC parameters on a scale of 0-100:
            1. completeness       - are all required fields, documents, and information present?
            2. accuracy           - does the information appear consistent, correct, and free of contradictions?
            3. processClarity     - is the process clearly described with identifiable steps and outcomes?
            4. documentationQuality - are the supporting documents sufficient and well-structured?
            5. taskCompletion     - were all tasks completed with adequate evidence?
            6. complianceReadiness - does the intake satisfy compliance and governance requirements?
            7. automationReadiness - is the process ready for automation assessment?

            Return ONLY a JSON object (no markdown fences) with this exact structure:
            {
              "overallScore": 85,
              "summary": "Brief overall assessment...",
              "parameters": [
                { "name": "completeness",      "score": 90, "label": "Completeness",          "comment": "..." },
                { "name": "accuracy",           "score": 85, "label": "Accuracy",               "comment": "..." },
                { "name": "processClarity",     "score": 80, "label": "Process Clarity",        "comment": "..." },
                { "name": "documentationQuality","score":75, "label": "Documentation Quality",  "comment": "..." },
                { "name": "taskCompletion",     "score": 90, "label": "Task Completion",        "comment": "..." },
                { "name": "complianceReadiness","score": 80, "label": "Compliance Readiness",   "comment": "..." },
                { "name": "automationReadiness","score": 70, "label": "Automation Readiness",   "comment": "..." }
              ],
              "strengths": ["strength 1", "strength 2"],
              "improvements": ["improvement 1", "improvement 2"]
            }
            """;

        var userMessage = BuildQcUserMessage(intake, analysisJson, tasksSummary, documentText);

        try
        {
            var requestBody = new
            {
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = userMessage }
                },
                max_tokens  = maxTokens,
                temperature = 0.3
            };

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var json     = JsonSerializer.Serialize(requestBody);
            var payload  = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await client.PostAsync(requestUrl, payload);
            var body     = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Azure OpenAI QC check failed ({Status}) for {IntakeId}: {Body}",
                    (int)response.StatusCode, intake.IntakeId, body);
                return GenerateMockQcResult(intake);
            }

            using var doc2     = JsonDocument.Parse(body);
            var msgContent = doc2.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "{}";

            return msgContent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure OpenAI QC check threw for {IntakeId}", intake.IntakeId);
            return GenerateMockQcResult(intake);
        }
    }

    private static string BuildQcUserMessage(
        IntakeRecord intake, string? analysisJson, string? tasksSummary, string? documentText)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== INTAKE RECORD ===");
        sb.AppendLine($"Intake ID: {intake.IntakeId}");
        sb.AppendLine($"Process: {intake.ProcessName}");
        sb.AppendLine($"Description: {intake.Description}");
        sb.AppendLine($"Business Unit: {intake.BusinessUnit}");
        sb.AppendLine($"Department: {intake.Department}");
        sb.AppendLine($"Process Owner: {intake.ProcessOwnerName} ({intake.ProcessOwnerEmail})");
        sb.AppendLine($"Type: {intake.ProcessType} | Priority: {intake.Priority}");
        sb.AppendLine($"Volume/Day: {intake.EstimatedVolumePerDay}");
        sb.AppendLine($"Location: {FormatGeoLocation(intake)}");
        sb.AppendLine($"Document Uploaded: {(string.IsNullOrWhiteSpace(intake.UploadedFileName) ? "No" : intake.UploadedFileName)}");

        if (!string.IsNullOrWhiteSpace(analysisJson))
        {
            sb.AppendLine();
            sb.AppendLine("=== AI ANALYSIS RESULT ===");
            sb.AppendLine(analysisJson.Length > MaxAnalysisJsonChars
                ? analysisJson[..MaxAnalysisJsonChars] + "\n[...truncated]"
                : analysisJson);
        }

        if (!string.IsNullOrWhiteSpace(tasksSummary))
        {
            sb.AppendLine();
            sb.AppendLine("=== TASK COMPLETION SUMMARY ===");
            sb.AppendLine(tasksSummary);
        }

        if (!string.IsNullOrWhiteSpace(documentText))
        {
            sb.AppendLine();
            sb.AppendLine("=== DOCUMENT CONTENT ===");
            sb.AppendLine(documentText.Length > MaxDocumentChars
                ? documentText[..MaxDocumentChars] + "\n[...truncated]"
                : documentText);
        }

        return sb.ToString();
    }

    private static string GenerateMockQcResult(IntakeRecord intake)
    {
        var hasDoc    = !string.IsNullOrWhiteSpace(intake.UploadedFileName);
        var hasOwner  = !string.IsNullOrWhiteSpace(intake.ProcessOwnerName);
        var hasDesc   = !string.IsNullOrWhiteSpace(intake.Description);
        var hasVolume = intake.EstimatedVolumePerDay > 0;

        int completeness   = (hasDoc ? 25 : 0) + (hasOwner ? 20 : 0) + (hasDesc ? 25 : 0) + (hasVolume ? 15 : 0) + 15;
        int accuracy       = hasDesc ? 80 : 55;
        int processClarity = hasDesc ? 75 : 50;
        int docQuality     = hasDoc  ? 78 : 45;
        int taskComp       = 85;
        int compliance     = 80;
        int automation     = intake.ProcessType == "Automated" ? 90 : intake.ProcessType == "Semi-Automated" ? 75 : 60;
        int overall        = (completeness + accuracy + processClarity + docQuality + taskComp + compliance + automation) / 7;

        return JsonSerializer.Serialize(new
        {
            overallScore = overall,
            summary = $"QC evaluation of '{intake.ProcessName}' yields an overall score of {overall}/100. " +
                      (overall >= 80 ? "The intake meets quality standards for closure." :
                       overall >= 60 ? "The intake partially meets quality standards — some improvements recommended." :
                                       "The intake requires significant improvements before it can be considered complete."),
            parameters = new[]
            {
                new { name = "completeness",       score = completeness,   label = "Completeness",          comment = hasDoc && hasOwner && hasDesc ? "All key fields and document are present." : "Some required information is missing." },
                new { name = "accuracy",            score = accuracy,       label = "Accuracy",               comment = hasDesc ? "Information appears consistent and well-defined." : "Process description is insufficient to assess accuracy." },
                new { name = "processClarity",      score = processClarity, label = "Process Clarity",        comment = hasDesc ? "Process steps and objectives are reasonably clear." : "Process description needs more detail." },
                new { name = "documentationQuality",score = docQuality,     label = "Documentation Quality",  comment = hasDoc  ? "Supporting document is attached and appears adequate." : "No supporting document uploaded." },
                new { name = "taskCompletion",      score = taskComp,       label = "Task Completion",        comment = "All assigned tasks have been completed or marked N/A." },
                new { name = "complianceReadiness", score = compliance,     label = "Compliance Readiness",   comment = "No compliance blockers identified based on available information." },
                new { name = "automationReadiness", score = automation,     label = "Automation Readiness",   comment = $"Process type is '{intake.ProcessType}' — assessed accordingly." }
            },
            strengths = new[]
            {
                hasOwner ? $"Process owner ({intake.ProcessOwnerName}) clearly assigned" : "Process is categorised and prioritised",
                hasDoc   ? "Supporting documentation provided" : $"Business unit ({intake.BusinessUnit}) identified"
            },
            improvements = new[]
            {
                !hasDoc  ? "Upload a detailed process documentation or SOP" : "Ensure document covers all exception handling scenarios",
                !hasDesc ? "Provide a comprehensive process description" : "Define measurable KPIs and SLA targets"
            }
        });
    }

    // ─── GenerateSopFromTranscriptAsync ──────────────────────────────────────

    public async Task<string> GenerateSopFromTranscriptAsync(string transcript, IntakeRecord intake)
    {
        var (endpoint, apiKey, deployment, apiVersion, maxTokens) = await GetAiConfigAsync();

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(deployment)
            || endpoint.Contains("YOUR_RESOURCE") || apiKey.Contains("YOUR_API_KEY")
            || deployment.Equals("YOUR_DEPLOYMENT_NAME", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Azure OpenAI not configured for tenant {TenantId} — returning mock SOP.", intake.TenantId);
            return GenerateMockSop(transcript, intake);
        }

        const int MaxTranscriptChars = 12000;
        var trimmedTranscript = transcript.Length > MaxTranscriptChars
            ? transcript[..MaxTranscriptChars] + "\n[... transcript truncated ...]"
            : transcript;

        var systemPrompt = $"""
            You are a senior business-process documentation specialist. Your task is to produce a
            COMPREHENSIVE, DETAILED Standard Operating Procedure (SOP) / Training Document from the
            provided meeting transcript. The document must be ready for immediate use by a new employee
            as a step-by-step training guide — not a high-level summary.

            Process context:
            - Process Name: {intake.ProcessName}
            - Business Unit: {intake.BusinessUnit}
            - Department: {intake.Department}
            - Process Owner: {intake.ProcessOwnerName}
            - Location: {intake.City}, {intake.Country}
            - Volume / Day: {intake.EstimatedVolumePerDay}
            - Priority: {intake.Priority}

            OUTPUT FORMAT (Markdown, use all sections below):

            # SOP — <Process Name>
            (Document header with Version, Effective Date, Owner, Department, Classification)

            ## 1. Purpose & Objectives
            (Detailed paragraph explaining WHY this process exists, what business outcome it delivers,
             and how success is measured.)

            ## 2. Scope
            (Who this applies to, what systems/teams are included, what is explicitly out of scope.)

            ## 3. Definitions & Acronyms
            (Table: Term | Definition — list every acronym and specialised term used in this process.)

            ## 4. Roles & Responsibilities (RACI)
            (Table: Role | Name/Team | Responsible | Accountable | Consulted | Informed
             Include at least 5 roles.)

            ## 5. Pre-requisites & Access Requirements
            (Numbered list: system accesses, training certifications, tools, data, permissions needed
             before starting. Include how to request each one.)

            ## 6. Process Overview (Flow Summary)
            (A short narrative — 3-5 sentences — describing the end-to-end flow before the detailed steps.)

            ## 7. Detailed Step-by-Step Procedure
            (For EACH step use this sub-structure:
               ### Step N: <Step Title>
               **Trigger / Input:** what kicks off this step
               **Actor:** who performs it
               **System / Tool:** which application or form to use
               **Actions (numbered sub-steps):**
                  1. Navigate to …
                  2. Enter / select …
                  3. Click / confirm …
               **Expected Output / Result:** what a correct completion looks like
               **Screenshot / Reference:** [Screenshot N — <description>]
               **Training Tip:** common mistakes and how to avoid them
             Include a minimum of 8 detailed steps extracted from the transcript.)

            ## 8. Quality Checkpoints & Validation
            (Numbered checklist of quality gates that must be passed before moving to the next stage.
             Include field-level validation rules where mentioned.)

            ## 9. Exception Handling & Escalation
            (Table: Exception Scenario | Likely Cause | Immediate Action | Escalation Path | SLA to Resolve
             Include at least 6 exception scenarios.)

            ## 10. Compliance & Regulatory Requirements
            (List any regulatory, audit, data-privacy, or SOX/GDPR requirements that apply to this
             process. Include record-retention periods if mentioned.)

            ## 11. Key Performance Indicators (KPIs) & SLAs
            (Table: Metric | Target | Measurement Frequency | Owner
             Include processing time, error rate, throughput, escalation response time.)

            ## 12. Tools & Systems Reference
            (Table: System / Tool | Purpose | Access Request Process | Support Contact)

            ## 13. Training & Certification
            (Describe the onboarding plan for a new team member: self-study, shadowing, supervised
             practice, sign-off criteria. List any mandatory certifications.)

            ## 14. Frequently Asked Questions (FAQ)
            (At least 6 Q&A pairs based on common issues mentioned in the transcript.)

            ## 15. Related Documents & References
            (Bullet list of related SOPs, policy documents, system manuals, or templates.)

            ## 16. Revision History
            (Table: Version | Date | Author | Change Summary)

            WRITING RULES:
            - Write in clear, direct language suitable for a new employee with no prior knowledge.
            - Every step must be actionable (start with a verb: "Open", "Enter", "Select", "Click").
            - Do NOT use vague phrases like "as appropriate" or "if necessary" without explaining when.
            - Extract ALL specific details from the transcript (system names, field names, team names,
              timeframes, thresholds, approval authorities).
            - Where the transcript is silent on a detail, write a realistic placeholder and mark it
              [TO CONFIRM] so the process owner knows to fill it in.
            - Aim for a document that a reader could follow on Day 1 with zero prior knowledge.
            """;

        var userMessage = $"Meeting transcript:\n\n{trimmedTranscript}";
        var requestUrl  = $"{endpoint.TrimEnd('/')}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";

        _logger.LogInformation("Generating SOP for {IntakeId} via {Url}", intake.IntakeId, requestUrl);

        try
        {
            var requestBody = new
            {
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = userMessage }
                },
                max_tokens  = Math.Max(maxTokens, 4000),
                temperature = 0.3
            };

            var client  = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var json     = JsonSerializer.Serialize(requestBody);
            var payload  = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await client.PostAsync(requestUrl, payload);
            var body     = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("SOP generation failed ({Status}) for {IntakeId}: {Body}",
                    (int)response.StatusCode, intake.IntakeId, body);
                return GenerateMockSop(transcript, intake);
            }

            using var doc = JsonDocument.Parse(body);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? GenerateMockSop(transcript, intake);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SOP generation failed for intake {IntakeId} (tenant {TenantId}).", intake.IntakeId, intake.TenantId);
            return GenerateMockSop(transcript, intake);
        }
    }

    private static string GenerateMockSop(string transcript, IntakeRecord intake) => $"""
        # SOP — {intake.ProcessName}

        **Version:** 1.0
        **Effective Date:** {DateTime.UtcNow:dd MMM yyyy}
        **Business Unit:** {intake.BusinessUnit}
        **Department:** {intake.Department}
        **Process Owner:** {intake.ProcessOwnerName}
        **Location:** {intake.City}, {intake.Country}
        **Classification:** Internal Use Only
        **Document Status:** Draft — Awaiting Process Owner Sign-off

        ---

        ## 1. Purpose & Objectives

        This Standard Operating Procedure defines the end-to-end execution of **{intake.ProcessName}**
        within the **{intake.BusinessUnit}** business unit, {intake.Department} department.
        The process has been captured from a recorded team meeting and formalised here as a training
        document for new and existing team members.

        **Objectives:**
        - Ensure consistent, accurate execution of {intake.ProcessName} across all team members
        - Reduce error rates and rework through clear step-level guidance
        - Meet defined SLA targets and compliance requirements
        - Provide a self-service training reference for onboarding new staff

        **Success Metrics:** Zero deviation from process steps during audits; error rate below 2%;
        all transactions completed within the agreed SLA window.

        ---

        ## 2. Scope

        **In Scope:**
        - All team members in {intake.Department} who execute or supervise {intake.ProcessName}
        - Estimated volume: approximately {intake.EstimatedVolumePerDay} transactions per day
        - Applicable systems, tools, and approval workflows described in this document

        **Out of Scope:**
        - Downstream processes that consume the output of this process [TO CONFIRM]
        - System administration or configuration changes
        - Processes handled by external vendors unless explicitly noted

        ---

        ## 3. Definitions & Acronyms

        | Term / Acronym | Definition |
        |----------------|-----------|
        | SOP | Standard Operating Procedure |
        | SLA | Service Level Agreement — the agreed time target for completing a task |
        | QC | Quality Control — review step to validate correctness before hand-off |
        | Intake | The formal submission of a process request into this system |
        | RAG | Retrieve–Augment–Generate — AI technique used to analyse process documents |
        | Process Owner | The named individual accountable for the outcome of this process |
        | Escalation | Raising an unresolved exception to a higher authority for resolution |
        | [TO CONFIRM] | Placeholder — process owner must supply this detail before publishing |

        ---

        ## 4. Roles & Responsibilities (RACI)

        | Role | Name / Team | Responsible (R) | Accountable (A) | Consulted (C) | Informed (I) |
        |------|-------------|:-:|:-:|:-:|:-:|
        | Process Owner | {intake.ProcessOwnerName} | | ✔ | ✔ | ✔ |
        | Team Member / Operator | {intake.Department} Team | ✔ | | | |
        | Team Lead / Supervisor | {intake.Department} Lead [TO CONFIRM] | | | ✔ | ✔ |
        | Quality Controller | QC Team [TO CONFIRM] | ✔ | | | |
        | IT Support | IT Helpdesk | | | ✔ | |
        | Compliance / Audit | Risk & Compliance Team | | | ✔ | ✔ |

        ---

        ## 5. Pre-requisites & Access Requirements

        Before executing this process ensure the following are in place:

        1. **System Access** — Active login credentials for all required systems [TO CONFIRM — list systems].
           Request via IT Service Desk ticket, allow 1–2 business days.
        2. **Mandatory Training** — Completion of induction training and this SOP read-and-sign-off.
        3. **Role Assignment** — Your manager must have assigned you the correct role/permission group.
        4. **Reference Data** — Current approved look-up tables and templates available in the shared drive.
        5. **Hardware / Tools** — Dual monitors recommended; stable VPN connection if working remotely.
        6. **Compliance Awareness** — Familiarity with the data-handling policy relevant to {intake.BusinessUnit}.

        ---

        ## 6. Process Overview (Flow Summary)

        The {intake.ProcessName} process begins when a trigger event is received (manual request, system
        alert, or scheduled batch). The operator validates the incoming data, processes it according to
        business rules, applies the required quality checks, and then hands off the output to the
        downstream team or system. Any exception is routed through the escalation path. The process
        concludes when the outcome is recorded in the system of record and all stakeholders are notified.

        ---

        ## 7. Detailed Step-by-Step Procedure

        ### Step 1: Receive & Log the Incoming Request
        **Trigger / Input:** Incoming request from upstream system, email, or queue  
        **Actor:** Operator  
        **System / Tool:** [Primary system — TO CONFIRM]  
        **Actions:**
        1. Open the task queue or inbox in [System Name — TO CONFIRM].
        2. Identify the new request — check priority flag and submission timestamp.
        3. Log the request reference number in the tracking register [TO CONFIRM — location].
        4. Acknowledge receipt by updating the status to "In Progress".
        **Expected Output:** Request logged with a unique reference number and status "In Progress".
        **Screenshot / Reference:** [Screenshot 1 — Queue screen with new request highlighted]  
        **Training Tip:** Do not skip the logging step. Missing entries cause downstream reconciliation failures.

        ### Step 2: Validate Input Data
        **Trigger / Input:** Logged request from Step 1  
        **Actor:** Operator  
        **System / Tool:** [Validation screen — TO CONFIRM]  
        **Actions:**
        1. Open the request detail view.
        2. Check all mandatory fields are populated (highlighted in red if missing).
        3. Cross-reference the customer/account number against the master data table.
        4. If data is complete and valid → proceed to Step 3.
        5. If data is incomplete → flag with status "Returned" and notify the requestor (use template email [TO CONFIRM]).
        **Expected Output:** Validated record ready for processing, or returned to requestor with clear reason.
        **Screenshot / Reference:** [Screenshot 2 — Validation screen with field highlights]  
        **Training Tip:** The most common error is accepting records with a blank "Account Reference". Always verify before proceeding.

        ### Step 3: Apply Business Rules & Process the Transaction
        **Trigger / Input:** Validated record from Step 2  
        **Actor:** Operator  
        **System / Tool:** [Processing module — TO CONFIRM]  
        **Actions:**
        1. Open the processing module and load the validated record.
        2. Select the correct transaction type from the drop-down list.
        3. Enter all required data fields according to the data-entry guide [TO CONFIRM — ref doc].
        4. Run the auto-calculation / auto-populate function if available.
        5. Review calculated values before saving — do not override without supervisor approval.
        6. Save the draft record (do NOT submit yet).
        **Expected Output:** Draft transaction record saved with all required fields populated.
        **Screenshot / Reference:** [Screenshot 3 — Processing form with completed fields]  
        **Training Tip:** Use the Tab key to move between fields to ensure no field is skipped.

        ### Step 4: Quality Control Review
        **Trigger / Input:** Draft record from Step 3  
        **Actor:** Operator (self-check) then QC Reviewer  
        **System / Tool:** QC checklist / review screen  
        **Actions:**
        1. Complete the self-check QC checklist (see Section 8).
        2. Attach any supporting documents required for audit.
        3. Route the record to the QC Reviewer via the "Submit for Review" button.
        4. QC Reviewer: open the review queue, open the record, verify against the QC checklist.
        5. Approve → proceed to Step 5. Reject → add rejection notes and return to operator.
        **Expected Output:** Record approved by QC Reviewer; status updated to "QC Passed".
        **Screenshot / Reference:** [Screenshot 4 — QC review screen with approve/reject buttons]  
        **Training Tip:** Every rejection must have written notes — vague rejections create rework loops.

        ### Step 5: Obtain Authorisation (if required)
        **Trigger / Input:** QC-passed record; authorisation threshold: [TO CONFIRM — e.g. >$10,000]  
        **Actor:** Team Lead / Authoriser  
        **System / Tool:** Approval workflow module  
        **Actions:**
        1. System automatically routes records above the threshold to the authoriser's queue.
        2. Authoriser reviews the record and supporting documentation.
        3. Authoriser approves (digital sign-off) or rejects with written justification.
        4. Operator receives notification of the decision.
        **Expected Output:** Approved record ready for submission, or rejection returned with reason.
        **Screenshot / Reference:** [Screenshot 5 — Approval queue and sign-off screen]  
        **Training Tip:** Records below the threshold skip this step automatically — do not manually escalate unless there is a quality concern.

        ### Step 6: Submit / Finalise the Transaction
        **Trigger / Input:** Authorised and QC-passed record  
        **Actor:** Operator  
        **System / Tool:** [Submission screen — TO CONFIRM]  
        **Actions:**
        1. Open the approved record.
        2. Click "Submit" / "Finalise" — confirm the submission dialog.
        3. System assigns a final transaction ID — record this in the tracking register.
        4. Status updates to "Complete" / "Submitted" automatically.
        **Expected Output:** Transaction successfully submitted; confirmation number generated.
        **Screenshot / Reference:** [Screenshot 6 — Confirmation screen with transaction ID]  
        **Training Tip:** Screenshot or note the transaction ID immediately — it is required for all future queries on this record.

        ### Step 7: Update Systems of Record & Notify Stakeholders
        **Trigger / Input:** Confirmed submission from Step 6  
        **Actor:** Operator  
        **System / Tool:** CRM / ERP / tracking register [TO CONFIRM]  
        **Actions:**
        1. Update the tracking register with final status, transaction ID, and completion timestamp.
        2. Send the completion notification to the requestor using the standard template.
        3. Update any linked systems (CRM, ERP, SharePoint list) with the outcome.
        4. Attach the final confirmation to the case/ticket.
        **Expected Output:** All systems updated; requestor notified; case closed.
        **Screenshot / Reference:** [Screenshot 7 — Updated tracking register row]  
        **Training Tip:** Notifications must be sent within 30 minutes of submission. Late notifications are a common audit finding.

        ### Step 8: End-of-Day Reconciliation
        **Trigger / Input:** End of business day or batch close  
        **Actor:** Team Lead  
        **System / Tool:** Reconciliation report [TO CONFIRM]  
        **Actions:**
        1. Run the daily reconciliation report for {intake.ProcessName}.
        2. Compare completed count against the queue count at start of day.
        3. Investigate any discrepancies — identify records in error or pending states.
        4. Escalate unresolved items to the process owner before close of business.
        5. Save and distribute the reconciliation report to stakeholders [TO CONFIRM — distribution list].
        **Expected Output:** Balanced reconciliation report; all exceptions documented.
        **Screenshot / Reference:** [Screenshot 8 — Reconciliation report balanced]  
        **Training Tip:** Never close the day with unexplained discrepancies — every open item needs an owner.

        ---

        ## 8. Quality Checkpoints & Validation

        Complete this checklist before progressing past each gate:

        **Pre-Processing Gate (before Step 3):**
        - [ ] Mandatory fields all populated — no blanks
        - [ ] Account/reference number verified against master data
        - [ ] Correct transaction type selected
        - [ ] No duplicate request already in the system

        **Pre-Submission Gate (before Step 6):**
        - [ ] QC checklist completed and signed off
        - [ ] Supporting documents attached
        - [ ] Authorisation obtained (if above threshold)
        - [ ] All calculated values reviewed and confirmed
        - [ ] Audit trail entry completed

        **End-of-Day Gate:**
        - [ ] Reconciliation report balanced
        - [ ] All exceptions assigned an owner with a resolution date
        - [ ] No records left in "Draft" status overnight

        ---

        ## 9. Exception Handling & Escalation

        | Exception Scenario | Likely Cause | Immediate Action | Escalation Path | Resolution SLA |
        |--------------------|-------------|-----------------|----------------|---------------|
        | Incomplete / invalid input data | Missing fields at source | Return to requestor with checklist | Level 1: Team Lead | Same business day |
        | Duplicate record detected | Upstream system error | Hold record; do not process | Level 1: Team Lead | 2 hours |
        | System unavailable (planned) | Maintenance window | Use offline contingency form | IT Support via helpdesk | Per maintenance schedule |
        | System unavailable (unplanned) | Outage | Log IT ticket; pause processing | Level 1: IT; Level 2: IT Manager | 4 hours |
        | Authorisation not received within SLA | Authoriser unavailable | Send reminder; escalate to deputy | Process Owner | 4 business hours |
        | Data quality failure at QC | Input error by operator | Return to operator for correction | Team Lead if recurring | 1 business day |
        | SLA breach risk (> 80% of window elapsed) | Volume spike or late start | Notify manager immediately | Process Owner | Immediate |
        | Regulatory / compliance flag | Data-privacy concern | Halt transaction; do not process | Compliance team | Immediate |

        ---

        ## 10. Compliance & Regulatory Requirements

        - All data processed under this procedure is subject to the organisation's Data Protection Policy [TO CONFIRM — policy ref].
        - Records must be retained for [TO CONFIRM — e.g. 7 years] in line with statutory requirements.
        - Any transaction above [TO CONFIRM — threshold] requires a dual-authorisation control.
        - Audit logs generated by the system are immutable and must not be altered.
        - Staff must complete annual data-handling refresher training before processing live transactions.
        - Personal data fields (names, account numbers) must not be copied to unencrypted media.

        ---

        ## 11. Key Performance Indicators (KPIs) & SLAs

        | Metric | Target | Measurement Frequency | Owner |
        |--------|--------|-----------------------|-------|
        | End-to-end processing time | ≤ [TO CONFIRM] hours per transaction | Daily | Process Owner |
        | Error / rework rate | < 2% of daily volume | Weekly | QC Lead |
        | SLA breach rate | 0% | Daily | Team Lead |
        | First-time-right rate | ≥ 95% | Weekly | Process Owner |
        | Authorisation turnaround | ≤ 4 business hours | Daily | Authoriser |
        | Daily reconciliation completion | 100% by [TO CONFIRM] each day | Daily | Team Lead |

        ---

        ## 12. Tools & Systems Reference

        | System / Tool | Purpose | Access Request | Support Contact |
        |--------------|---------|---------------|----------------|
        | [Primary System — TO CONFIRM] | Core transaction processing | IT Service Desk | IT Helpdesk |
        | Tracking Register (SharePoint / Excel) | Logging and reconciliation | Team Lead | Team Lead |
        | Email Client | Notifications and communications | IT Service Desk | IT Helpdesk |
        | QC Review Module | Quality review and approval | Team Lead request | IT Helpdesk |
        | IQFlow Agent | Process intake and AI analysis | System Administrator | IQFlow Support |

        ---

        ## 13. Training & Certification

        **New Starter Onboarding Plan:**

        | Week | Activity | Delivery | Sign-off Required |
        |------|----------|----------|------------------|
        | Week 1 | Read this SOP and all referenced documents | Self-study | Acknowledgement form |
        | Week 1 | Attend induction session with Team Lead | Classroom / virtual | N/A |
        | Week 2 | Shadow an experienced team member for full process | On-the-job | Team Lead |
        | Week 2 | Complete test transactions in sandbox environment | Supervised practice | Team Lead |
        | Week 3 | Process live transactions with supervisor check | Supervised live | Supervisor |
        | Week 4 | Independent processing with spot-checks | Independent | Process Owner |

        **Mandatory Certifications:** [TO CONFIRM — list any compliance or system certifications required]

        ---

        ## 14. Frequently Asked Questions (FAQ)

        **Q1: What do I do if a mandatory field is greyed out and I cannot populate it?**  
        A: This usually means a prerequisite field earlier in the form has not been completed. Scroll up and check for blank required fields. If the issue persists, raise an IT ticket.

        **Q2: Can I process a record even if it has not yet been QC approved?**  
        A: No. QC approval is a mandatory gate. Processing without approval is a control violation and will be flagged in the next audit.

        **Q3: What happens if I accidentally submit a record with incorrect data?**  
        A: Contact your Team Lead immediately. Depending on the stage, a reversal or amendment may be possible. Do not attempt to fix it yourself without guidance.

        **Q4: Who do I contact if I cannot reach the authoriser within the SLA?**  
        A: Escalate to the Process Owner ({intake.ProcessOwnerName}). If unavailable, escalate to the next-level manager. Always document the escalation in the tracking register.

        **Q5: How long should I keep local copies of processed records?**  
        A: You should not keep local copies. All records must be stored in the designated system of record. Local copies are a data-security risk.

        **Q6: The system is showing a duplicate warning — should I process anyway?**  
        A: No. Hold the record and consult your Team Lead. Duplicate processing is a frequent audit finding and can cause financial or compliance issues.

        ---

        ## 15. Related Documents & References

        - Data Protection & Privacy Policy [TO CONFIRM — link]
        - IT Access Request Procedure [TO CONFIRM — link]
        - Business Continuity / Contingency Plan for {intake.ProcessName} [TO CONFIRM — link]
        - QC Checklist Template [TO CONFIRM — link]
        - Escalation Contact Directory [TO CONFIRM — link]
        - Onboarding Induction Pack — {intake.BusinessUnit} [TO CONFIRM — link]

        ---

        ## 16. Revision History

        | Version | Date | Author | Change Summary |
        |---------|------|--------|---------------|
        | 1.0 | {DateTime.UtcNow:dd MMM yyyy} | AI-Generated (IQFlow Agent) | Initial draft generated from meeting transcript |

        ---

        *This document was auto-generated from a meeting recording by IQFlow Agent.
        It must be reviewed and validated by {intake.ProcessOwnerName} before being published as an official SOP.
        All [TO CONFIRM] placeholders must be resolved prior to final sign-off.*

        > **Note:** This is a mock SOP generated without Azure Speech + OpenAI credentials.
        > Configure Azure Speech and Azure OpenAI in Tenant AI Settings to produce AI-generated content
        > directly extracted from your recorded meeting transcript.
        """;

    // ── TestConnectionAsync ────────────────────────────────────────────────────

    /// <summary>
    /// Sends a lightweight test prompt to the configured Azure OpenAI endpoint and
    /// returns whether it received a successful HTTP 200 response.
    /// </summary>
    public async Task<(bool success, int statusCode, string message)> TestConnectionAsync()
    {
        var (endpoint, apiKey, deployment, apiVersion, _) = await GetAiConfigAsync();

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(deployment)
            || endpoint.Contains("YOUR_RESOURCE") || apiKey.Contains("YOUR_API_KEY")
            || deployment.Equals("YOUR_DEPLOYMENT_NAME", StringComparison.OrdinalIgnoreCase))
        {
            return (false, 0, "Azure OpenAI is not configured. Please fill in Endpoint, API Key and Deployment Name.");
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            return (false, 0, $"Endpoint '{endpoint}' is not a valid URL.");

        var requestUrl = $"{endpoint.TrimEnd('/')}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";

        var requestBody = new
        {
            messages = new object[]
            {
                new { role = "user", content = "Reply with the single word: OK" }
            },
            max_tokens  = 5,
            temperature = 0
        };

        try
        {
            var http = _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);

            var response = await http.PostAsJsonAsync(requestUrl, requestBody);
            int code = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Azure OpenAI connection test succeeded for deployment '{Deployment}'.", deployment);
                return (true, code, $"Connected successfully to deployment '{deployment}' (HTTP {code}).");
            }

            var body = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Azure OpenAI connection test returned HTTP {Code}: {Body}", code, body);

            var hint = code switch
            {
                401 => "Authentication failed — check your API Key.",
                403 => "Access denied — verify subscription and permissions.",
                404 => $"Deployment '{deployment}' not found — check the name in Azure AI Foundry.",
                429 => "Rate limit or quota exceeded.",
                _   => $"HTTP {code} — check your endpoint URL and credentials."
            };
            return (false, code, hint);
        }
        catch (TaskCanceledException)
        {
            return (false, 0, "Request timed out (15 s). Verify the endpoint URL is reachable.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure OpenAI connection test threw an exception.");
            return (false, 0, $"Connection error: {ex.Message}");
        }
    }

    /// <summary>
    /// Injects a <c>_source</c> field into a JSON object string without full re-serialization.
    /// Falls back to the original string if parsing fails.
    /// </summary>
    private static string InjectSourceField(string json, string source)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return json;

            using var ms     = new System.IO.MemoryStream();
            using var writer = new Utf8JsonWriter(ms);
            writer.WriteStartObject();
            writer.WriteString("_source", source);
            foreach (var prop in doc.RootElement.EnumerateObject())
                prop.WriteTo(writer);
            writer.WriteEndObject();
            writer.Flush();
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            return json;
        }
    }

    // ── GenerateDescriptionAsync ───────────────────────────────────────────────
    /// <summary>
    /// Expands brief pointers into a detailed, professional process description.
    /// Returns the generated text, or an empty string when AI is not configured / unavailable.
    /// </summary>
    public async Task<string> GenerateDescriptionAsync(string processName, string pointers)
    {
        var (endpoint, apiKey, deployment, apiVersion, _) = await GetAiConfigAsync();

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(deployment)
            || endpoint.Contains("YOUR_RESOURCE") || apiKey.Contains("YOUR_API_KEY")
            || deployment.Equals("YOUR_DEPLOYMENT_NAME", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Azure OpenAI not configured — cannot generate description for '{ProcessName}'.", processName);
            return string.Empty;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
        {
            _logger.LogWarning("Azure OpenAI endpoint '{Endpoint}' is invalid — cannot generate description.", endpoint);
            return string.Empty;
        }

        var requestUrl = $"{endpoint.TrimEnd('/')}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";

        const string systemPrompt = """
            You are a business process analyst. The user has provided a process name and brief key points.
            Expand these points into a comprehensive, professional process description (150-300 words).
            The description should clearly explain what the process does, who is involved, the key steps
            and inputs/outputs, and the business value it delivers.
            Use clear, professional language and flowing prose paragraphs — NOT bullet points.
            Return ONLY the description text. Do not include headings, labels, or extra commentary.
            """;

        var userMessage = string.IsNullOrWhiteSpace(processName)
            ? $"Key points:\n{pointers}"
            : $"Process Name: {processName}\n\nKey points:\n{pointers}";

        try
        {
            var requestBody = new
            {
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = userMessage }
                },
                max_tokens  = 600,
                temperature = 0.7,
                top_p       = 1.0,
                model       = deployment
            };

            var http = _httpClientFactory.CreateClient();
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);

            var response = await http.PostAsJsonAsync(requestUrl, requestBody);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("Azure OpenAI description generation returned {Status}: {Body}",
                    (int)response.StatusCode, errorBody);
                return string.Empty;
            }

            var responseText = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseText);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return content?.Trim() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure OpenAI description generation threw for process '{ProcessName}'.", processName);
            return string.Empty;
        }
    }
}
