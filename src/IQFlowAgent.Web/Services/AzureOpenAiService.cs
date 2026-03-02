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

    public AzureOpenAiService(IConfiguration config, ILogger<AzureOpenAiService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string> AnalyzeIntakeAsync(IntakeRecord intake, string? documentText)
    {
        var endpoint  = _config["AzureOpenAI:Endpoint"];
        var apiKey    = _config["AzureOpenAI:ApiKey"];
        var deployment = _config["AzureOpenAI:DeploymentName"];
        var apiVersion = _config["AzureOpenAI:ApiVersion"] ?? "2025-01-01-preview";
        var maxTokens  = int.TryParse(_config["AzureOpenAI:MaxTokens"], out var mt) ? mt : DefaultMaxOutputTokens;

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

            return string.IsNullOrWhiteSpace(content)
                ? GenerateMockAnalysis(intake)
                : content;
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
        var endpoint   = _config["AzureOpenAI:Endpoint"];
        var apiKey     = _config["AzureOpenAI:ApiKey"];
        var deployment = _config["AzureOpenAI:DeploymentName"];
        var apiVersion = _config["AzureOpenAI:ApiVersion"] ?? "2025-01-01-preview";
        var maxTokens  = int.TryParse(_config["AzureOpenAI:MaxTokens"], out var mt) ? mt : DefaultMaxOutputTokens;

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

            return string.IsNullOrWhiteSpace(content)
                ? GenerateMockVerification(intake, tasks)
                : content;
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
        var endpoint   = _config["AzureOpenAI:Endpoint"];
        var apiKey     = _config["AzureOpenAI:ApiKey"];
        var deployment = _config["AzureOpenAI:DeploymentName"];
        var apiVersion = _config["AzureOpenAI:ApiVersion"] ?? "2025-01-01-preview";
        var maxTokens  = int.TryParse(_config["AzureOpenAI:MaxTokens"], out var mt) ? mt : DefaultMaxOutputTokens;

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
        var endpoint   = _config["AzureOpenAI:Endpoint"];
        var apiKey     = _config["AzureOpenAI:ApiKey"];
        var deployment = _config["AzureOpenAI:DeploymentName"];
        var apiVersion = _config["AzureOpenAI:ApiVersion"] ?? "2025-01-01-preview";
        var maxTokens  = int.TryParse(_config["AzureOpenAI:MaxTokens"], out var mt) ? mt : DefaultMaxOutputTokens;

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
}
