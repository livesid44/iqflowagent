using IQFlowAgent.Web.Models;
using System.Net.Http.Headers;
using System.Text.Json;

namespace IQFlowAgent.Web.Services;

public class AzureOpenAiService : IAzureOpenAiService
{
    private const int MaxDocumentChars = 8000;
    private const int DefaultMaxOutputTokens = 2000;
    private const int MaxAnalysisJsonChars = 4000;  // max chars from AI analysis sent in field-analysis prompt
    // 20 000 chars covers multiple multi-sheet Excel files + Word docs + task comments while
    // staying comfortably within the 128 k-token context window of gpt-4o / gpt-4-turbo.
    private const int MaxArtifactCharsPerReport = 20000;

    private readonly IConfiguration _config;
    private readonly ILogger<AzureOpenAiService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITenantContextService _tenantContext;
    private readonly IPiiScanService _piiScanner;
    private readonly IAuditLogService _auditLog;

    // Accumulates PII findings detected during the most recent AnalyzeIntakeAsync call.
    // Reset to empty at the start of each AnalyzeIntakeAsync invocation.
    // Thread-safety note: AzureOpenAiService is registered as Scoped — every HTTP request
    // and every background-job scope resolves a separate instance, so _lastAnalysisPiiFindings
    // is never accessed concurrently from different threads.
    private readonly List<PiiFinding> _lastAnalysisPiiFindings = new();

    // ── Audit-log context ─────────────────────────────────────────────────────
    // Set at the start of each public method so the PII helper can log correctly.
    private string _currentCorrelationId = string.Empty;
    private int?   _currentIntakeId;

    public AzureOpenAiService(IConfiguration config, ILogger<AzureOpenAiService> logger,
        IHttpClientFactory httpClientFactory, ITenantContextService tenantContext,
        IPiiScanService piiScanner, IAuditLogService auditLog)
    {
        _config = config;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _tenantContext = tenantContext;
        _piiScanner = piiScanner;
        _auditLog = auditLog;
    }

    /// <inheritdoc />
    public IReadOnlyList<PiiFinding> GetLastPiiFindings() => _lastAnalysisPiiFindings.AsReadOnly();

    private async Task<(string? endpoint, string? apiKey, string? deployment, string apiVersion, int maxTokens, string modelVersion)> GetAiConfigAsync()
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
                tenantSettings.AzureOpenAIMaxTokens,
                !string.IsNullOrWhiteSpace(tenantSettings.AzureOpenAIModelVersion)
                    ? tenantSettings.AzureOpenAIModelVersion : "gpt-5.2");
        }
        var endpoint = _config["AzureOpenAI:Endpoint"];
        var apiKey = _config["AzureOpenAI:ApiKey"];
        var deployment = _config["AzureOpenAI:DeploymentName"];
        var apiVersion = _config["AzureOpenAI:ApiVersion"] ?? "2025-01-01-preview";
        var maxTokens = int.TryParse(_config["AzureOpenAI:MaxTokens"], out var mt) ? mt : DefaultMaxOutputTokens;
        var modelVersion = _config["AzureOpenAI:ModelVersion"] ?? "gpt-5.2";
        return (endpoint, apiKey, deployment, apiVersion, maxTokens, modelVersion);
    }

    public async Task<string> AnalyzeIntakeAsync(IntakeRecord intake, string? documentText)
    {
        // Reset findings so GetLastPiiFindings() reflects only this invocation.
        _lastAnalysisPiiFindings.Clear();
        _currentCorrelationId = Guid.NewGuid().ToString("N")[..12];
        _currentIntakeId      = intake.Id;

        var (endpoint, apiKey, deployment, apiVersion, maxTokens, modelVersion) = await GetAiConfigAsync();

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
                You are an expert business process analyst specialising in BARTOK Schedule 8 SOP documentation.
                IMPORTANT: You MUST base ALL of your analysis exclusively on the information provided in this prompt —
                the intake metadata and any uploaded document content. Do NOT use any external knowledge from the internet,
                Wikipedia, industry databases, or other outside sources. Only rephrase, summarize, and structure the
                information that is explicitly present in the provided intake data and document text.

                The END GOAL of this workflow is to produce a completed BARTOK S8 SOP document.
                That document has the following sections — each requiring specific information:

                  Document Control     : Process name, lot/SDC, document date, process author (name + email), approver (name + email)
                  1. Purpose & Scope   : Countries in scope, input artefacts / documents required
                  2. Process Overview  : Detailed description, process owner, monthly volumes (12 months of data), peak volume/period, weekday/weekend/holiday hours of operation, systems used
                  3. RACI              : 4 task names specific to this process, 4 role titles, RACI assignments (R/A/C/I per cell)
                  4. SOP Steps         : Step-by-step actions, responsible role per step, system used per step, expected output per step, decision points, automation status, opportunity rating, automation type
                  5. Work Instructions : Detailed step-by-step instructions (including navigation, field entries, error handling) for each SOP step
                  6. Escalation & Exceptions : Escalation triggers, escalation paths, resolution timeframes, exception types, exception handling approach, approval requirements
                  7. SLAs & Performance: SLA metric names, measurement methods, reporting frequency, measurement tools, actual vs target performance data
                  8. Volumetrics       : Transaction type(s) and volume detail, volume notes (peaks/anomalies/seasonal factors), volume forecast
                  9. Regulatory & Compliance : Applicable regulations/standards, obligations, controls in the process, evidence artefacts, TechM Framework references
                  10. Training         : Training module names, delivery methods, competency verification approach
                  11. OCC              : Orange Customer Contract reference numbers, obligations, how the process addresses each obligation

                YOUR TASK:
                1. Review the intake form and uploaded document.
                2. For EACH section listed above, determine whether the available information is sufficient (Pass), partial (Warning), or missing (Fail).
                3. For every section that is Warning or Fail, create an actionItem that specifies exactly what information must be gathered.
                4. Produce checkPoints — one per BARTOK section — assessing its completeness.

                Respond ONLY with valid JSON (no markdown fences) matching this exact structure:
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
                    {
                      "title": "action title",
                      "description": "what needs to be done",
                      "owner": "role or team",
                      "priority": "High|Medium|Low",
                      "bartokSection": "2. Process Overview",
                      "requiredInfo": "Monthly transaction volumes for 12 months; peak volume and period; weekday/weekend/holiday hours of operation; list of systems used"
                    }
                  ],
                  "checkPoints": [
                    { "label": "checkpoint label", "status": "Pass|Fail|Warning", "note": "optional explanation" }
                  ],
                  "qualityScore": 90,
                  "summary": "Brief executive summary of the process analysis."
                }

                Rules for actionItems:
                - Only create an action item for a section where information is genuinely missing or insufficient.
                - Set priority=High for critical sections: Document Control, 2. Process Overview, 4. SOP Steps.
                - Set priority=Medium for supporting sections: 3. RACI, 5. Work Instructions, 6. Escalation & Exceptions, 7. SLAs & Performance, 8. Volumetrics.
                - Set priority=Low for sections that can be confirmed later: 9. Regulatory & Compliance, 10. Training, 11. OCC.
                - "bartokSection" must exactly name the section (e.g. "4. SOP Steps").
                - "requiredInfo" must list the specific data items that are absent or unconfirmed.
                - If the uploaded document clearly covers a section, do NOT create a task for it.

                Rules for checkPoints:
                - Include one checkpoint per BARTOK section (11 total) assessing whether its information is sufficient.
                - status=Pass: enough information is present in the intake or document.
                - status=Warning: partial information; some items need confirmation from the process owner.
                - status=Fail: section is critically under-documented; a task must be created.

                actionItems: concrete steps to collect missing information required for the BARTOK S8 SOP output document.
                checkPoints: section-level readiness checks for the BARTOK S8 SOP output document.
                """;

            var requestBody = new
            {
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = await EnforcePiiPolicyAsync(BuildUserMessage(intake, documentText), "AnalyzeIntake") }
                },
                max_tokens  = maxTokens,
                temperature = 0.7,
                top_p       = 1.0,
                model       = modelVersion   // hint to the Azure OpenAI API about the desired model version
            };

            var http = _httpClientFactory.CreateClient();
            // Azure OpenAI (cognitiveservices.azure.com) authenticates via the "api-key" header.
            http.DefaultRequestHeaders.Add("api-key", apiKey);

            var response = await PostLlmAsync(http, requestUrl, requestBody, "AnalyzeIntake");

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

            return InjectSourceField(StripMarkdownFences(content), "llm");
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
        _currentCorrelationId = Guid.NewGuid().ToString("N")[..12];
        _currentIntakeId      = intake.Id;
        var (endpoint, apiKey, deployment, apiVersion, maxTokens, modelVersion) = await GetAiConfigAsync();

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
                IMPORTANT: You MUST base ALL of your verification exclusively on the task comments, action logs, and
                artifact information explicitly provided in this prompt. Do NOT use any external knowledge from the
                internet or outside sources. Only evaluate the evidence that is explicitly present in the provided data.
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
                    new { role = "user",   content = await EnforcePiiPolicyAsync(BuildVerificationMessage(intake, tasks, aggregatedArtifactText), "VerifyClosure") }
                },
                max_tokens  = maxTokens,
                temperature = 0.3,
                top_p       = 1.0,
                model       = modelVersion
            };

            var http = _httpClientFactory.CreateClient();
            http.DefaultRequestHeaders.Add("api-key", apiKey);

            var response = await PostLlmAsync(http, requestUrl, requestBody, "VerifyIntakeClosure");

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

            return InjectSourceField(StripMarkdownFences(content), "llm");
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
                .Where(l => !string.IsNullOrWhiteSpace(l.Comment))
                .OrderBy(l => l.CreatedAt)
                .ToList();
            if (comments.Count > 0)
            {
                sb.AppendLine("Notes and comments:");
                foreach (var c in comments)
                {
                    var prefix = c.ActionType == "StatusChange"
                        ? $"[Status → {c.NewStatus}]"
                        : "[Comment]";
                    sb.AppendLine($"  - [{c.CreatedAt:yyyy-MM-dd HH:mm}] {prefix} {c.Comment}");
                }
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
        _currentCorrelationId = Guid.NewGuid().ToString("N")[..12];
        _currentIntakeId      = intake.Id;
        var (endpoint, apiKey, deployment, apiVersion, maxTokens, modelVersion) = await GetAiConfigAsync();

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
            You are an expert BARTOK / Schedule 8 SOP documentation specialist at TechM.
            IMPORTANT: You MUST base ALL content exclusively on the information provided in this prompt —
            the intake metadata, any uploaded document text, and the TASK NOTES, COMMENTS AND UPLOADED DOCUMENT
            CONTENT section. Do NOT use any external knowledge from the internet, Wikipedia, regulations
            databases, or other outside sources.

            DATA PRIORITY ORDER (most authoritative first):
            1. INTAKE DOCUMENTS AND TASK NOTES / COMMENTS — this section contains the original
               documents uploaded with the intake, files attached when completing tasks, and notes
               written by the process owner. It is the MOST IMPORTANT source. Always extract and
               use values from here if they are present.
            2. INTAKE INFORMATION — the original intake form data.
            3. PRIOR AI ANALYSIS — use only as supporting reference.

            You will be given structured intake data about a business process and a list of fields
            from the new BARTOK S8 SOP template that must be filled in.

            The template covers these sections:
            - Document Control (process name, lot, date, author, approver)
            - 1. Purpose and Scope (countries in scope, input artefacts)
            - 2. Process Overview (description, owner, volumes, hours of operation, systems used)
            - 3. RACI (4 tasks × 4 roles — generate specific task names and role titles)
            - 4. Standard Operating Procedure (step actions, roles, systems, outputs, decision point, automation assessment)
            - 5. Work Instructions (step-by-step instructions for each SOP step)
            - 6. Escalation and Exception Handling (triggers, paths, timeframes, exception types)
            - 7. Service Levels and Performance (SLA metrics, measurement methods, historical actuals)
            - 8. Volumetrics (transaction volumes, peak notes, forecast)
            - 9. Regulatory and Compliance (applicable regulations, controls, evidence)
            - 10. Training Materials (modules, delivery methods, competency verification)
            - 11. Orange Customer Contract Obligations (OCC references, obligations, controls)

            Your goal: fill EVERY SINGLE field with meaningful, professional content derived solely
            from the provided intake data and document content.
            This is a full document generation — every field must contain specific, relevant content.

            Rules:
            1. For fields directly from intake (process name, owner, country, etc.) — use them exactly.
            2. For process description, SOP steps, work instructions, and SLA metrics — generate content
               that is grounded in and consistent with the provided intake data and document text only.
               Be concrete and specific to this process, not generic.
            3. For RACI tasks and roles — derive task names and role titles from the intake data and document.
            4. For SOP steps — derive step actions from the process description and document content.
               Include the role, system, and expected output only if mentioned in the provided data.
            5. For work instructions — write step-by-step instructions based on what is described in the document.
            6. For volumetrics / monthly volumes — if monthly volume data is present in TASK NOTES or
               uploaded files (e.g. an Excel table), extract and use those exact numbers. Do NOT write
               "To be confirmed" if volume data is present anywhere in the provided content.
            7. For SLA metrics — use only SLA/KPI data mentioned in the intake, task notes, or document.
               If none are present anywhere in the provided content, write "To be confirmed with process owner."
            8. For regulatory/compliance — use only regulatory references explicitly mentioned in the intake,
               task notes, or document. Do NOT infer regulations from the geography or industry. If none are
               stated anywhere in the provided content, write "To be confirmed with the compliance team."
            9. NEVER use "Missing" status. For EVERY field, always return "Available" with real content.
               If exact data is truly not available anywhere in the provided content, write professional
               placeholder text that a reviewer can easily update (e.g. "To be confirmed with process owner").
            10. You MUST return a JSON entry for EVERY field key provided — do not omit any field.
            11. Keep fill values concise: 1-2 sentences for simple fields, 3-4 sentences for narrative fields.
            12. When the task notes or document contain tabular data (e.g. monthly volumes in rows), read
                each row and include ALL the values in your response for the relevant field.

            Respond ONLY with valid JSON matching this structure (no markdown fences):
            {
              "fields": [
                {
                  "key": "field_key",
                  "status": "Available",
                  "fillValue": "the value to insert in the document",
                  "notes": "brief explanation of source"
                }
              ]
            }
            """;

        var userMessage = BuildFieldAnalysisMessage(intake, fieldDefinitionsJson, analysisJson, artifactText);

        var requestBody = new
        {
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = await EnforcePiiPolicyAsync(userMessage, "AnalyzeReportFields") }
            },
            max_tokens  = Math.Max(maxTokens, 6000),  // need space for full-document field coverage
            temperature = 0.3,
            top_p       = 1.0,
            model       = modelVersion
        };

        try
        {
            var http = _httpClientFactory.CreateClient();
            http.DefaultRequestHeaders.Add("api-key", apiKey);

            var response = await PostLlmAsync(http, requestUrl, requestBody, "AnalyzeReportFields");

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
                : StripMarkdownFences(content);
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
        sb.AppendLine($"Lots / SDC: {(string.IsNullOrWhiteSpace(intake.SdcLots) ? "(none)" : intake.SdcLots)}");
        sb.AppendLine();

        sb.AppendLine("=== TEMPLATE FIELDS TO ANALYZE ===");
        sb.AppendLine(fieldDefinitionsJson);

        if (!string.IsNullOrWhiteSpace(analysisJson))
        {
            sb.AppendLine();
            sb.AppendLine("=== PRIOR AI ANALYSIS (for reference) ===");
            var truncated = analysisJson.Length > MaxAnalysisJsonChars
                ? analysisJson[..MaxAnalysisJsonChars] + "\n[...truncated]"
                : analysisJson;
            sb.AppendLine(truncated);
        }

        if (!string.IsNullOrWhiteSpace(artifactText))
        {
            sb.AppendLine();
            sb.AppendLine("=== INTAKE DOCUMENTS AND TASK NOTES / COMMENTS ===");
            sb.AppendLine("IMPORTANT: The following content comes from documents uploaded for this intake");
            sb.AppendLine("(original intake uploads AND files attached when completing tasks) PLUS any notes");
            sb.AppendLine("written by the process owner when completing tasks.");
            sb.AppendLine("It is the PRIMARY source for filling fields. Extract ALL relevant values directly from this text.");
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

    /// <summary>
    /// Returns an ("Available", fillValue, notes) tuple with professional DD placeholder text,
    /// used whenever a concrete value is not available from intake data.
    /// </summary>
    private static (string status, string fillValue, string notes) Placeholder(
        string label, string confirmer = "the process owner", string? extra = null)
    {
        var fill = $"{label} to be confirmed with {confirmer} during the DD walkthrough."
                   + (string.IsNullOrWhiteSpace(extra) ? "" : $" {extra}");
        return ("Available", fill, $"Synthesised — confirm {label.ToLower()} with {confirmer}.");
    }

    private static (string status, string fillValue, string notes) ResolveMockField(
        string key, string label, string source, IntakeRecord intake, string? analysisJson)
    {
        // Auto-resolve from intake properties
        var intakeValue = source switch
        {
            "ProcessName"          => intake.ProcessName,
            "BusinessUnit"         => intake.BusinessUnit,
            "Department"           => intake.Department,
            "ProcessOwnerName"     => intake.ProcessOwnerName,
            "ProcessOwnerContact"  => $"{intake.ProcessOwnerName} | {intake.ProcessOwnerEmail}",
            "ProcessType"          => intake.ProcessType,
            "TimeZone"             => intake.TimeZone,
            "Description"          => intake.Description,
            "UploadedFileName"     => intake.UploadedFileName ?? "",
            "Country"              => intake.Country,
            "SdcLots"              => intake.SdcLots,
            "GeoLocation"          => FormatGeoLocation(intake),
            "TODAY"                => DateTime.UtcNow.ToString("dd-MMM-yyyy"),
            _                      => null
        };

        if (intakeValue != null)
        {
            return string.IsNullOrWhiteSpace(intakeValue)
                ? Placeholder(label, extra: "Value not supplied in intake — please update.")
                : ("Available", intakeValue, $"Auto-resolved from intake: {source}");
        }

        // Resolve from AI analysis JSON
        if (source.StartsWith("AI:", StringComparison.OrdinalIgnoreCase))
        {
            var aiProp = source["AI:".Length..];

            // Try to pull from the AI analysis JSON first (if present)
            if (!string.IsNullOrWhiteSpace(analysisJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(analysisJson);
                    var root = doc.RootElement;
                    if (aiProp == "summary" && root.TryGetProperty("summary", out var sum))
                        return ("Available", sum.GetString() ?? "", "Sourced from AI process analysis summary.");
                    if (aiProp == "confidenceScore" && root.TryGetProperty("confidenceScore", out var cs))
                        return ("Available", cs.GetRawText(), "Sourced from AI confidence score.");
                }
                catch { /* ignore */ }
            }

            // Synthesise from intake metadata — covers the mock/fallback path and enriches the LLM call context
            var location = FormatGeoLocation(intake);
            var vol      = intake.EstimatedVolumePerDay;
            var procName = intake.ProcessName;
            var bu       = intake.BusinessUnit;
            var owner    = intake.ProcessOwnerName;
            var ptype    = intake.ProcessType;
            var tz       = intake.TimeZone;
            var country  = intake.Country;
            var desc     = string.IsNullOrWhiteSpace(intake.Description) ? $"the {procName} process" : intake.Description;

            return aiProp switch
            {
                "summary" =>
                    ("Available",
                     $"{procName} is a {ptype?.ToLower()} business process operated by the {bu} team. " +
                     $"{desc} The process runs in {location} and handles approximately {vol} transactions per day.",
                     "Synthesised from intake metadata."),

                "processContext" =>
                    ("Available",
                     $"This process is managed by {owner} within the {bu} department. " +
                     $"It is classified as '{ptype}' and operates out of {location}. " +
                     $"Review any recent changes or pain points with the process owner before finalising this section.",
                     "Synthesised from intake metadata."),

                "flowSummary" =>
                    ("Available",
                     $"{procName} is triggered by incoming requests handled by the {bu} team in {location}. " +
                     $"The process follows a {ptype?.ToLower()} model and processes approximately {vol} transactions per day. " +
                     $"Key decision points and exception paths should be validated with the process owner during the DD walkthrough.",
                     "Synthesised from intake summary."),

                "operatingModel" =>
                    ("Available",
                     $"The {procName} process is operated by the {bu} team under {owner}. " +
                     $"The team is based in {location} and follows a {ptype?.ToLower()} delivery model. " +
                     $"Estimated volume is {vol} transactions per day. Specific headcount, reporting lines, and shared-service arrangements to be confirmed with the process owner.",
                     "Synthesised from intake metadata."),

                "peakVolume" =>
                    ("Available",
                     $"Est. {vol} transactions / day. Confirm peak period (e.g. month-end, quarter-end) with process owner.",
                     "Synthesised from intake volume data."),

                "witoSummary" =>
                    ("Available",
                     $"The transition of {procName} to TechM will require knowledge transfer, tooling alignment, and SLA validation. " +
                     $"The {bu} team currently operates from {location}; continuity planning should address geo-specific dependencies. " +
                     $"Key risks include knowledge concentration and process standardisation across locations. " +
                     $"Confirm specific transition milestones and Day 1 readiness criteria with {owner}.",
                     "Synthesised from intake metadata."),

                "subProcess" =>
                    string.IsNullOrWhiteSpace(desc)
                        ? Placeholder(label)
                        : ("Available", "N/A", "No distinct sub-process identified from intake data."),

                "processCategory" =>
                    string.IsNullOrWhiteSpace(ptype)
                        ? Placeholder(label, extra: "Select from: Service Delivery / Service Assurance / Service Management / Billing & Invoicing.")
                        : ("Available",
                           ptype.Contains("Delivery", StringComparison.OrdinalIgnoreCase) ? "Service Delivery" :
                           ptype.Contains("Assurance", StringComparison.OrdinalIgnoreCase) ? "Service Assurance" :
                           ptype.Contains("Billing", StringComparison.OrdinalIgnoreCase) ? "Billing & Invoicing" : "Service Management",
                           "Mapped from intake process type."),

                "shiftModel" =>
                    ("Available",
                     tz?.Contains("GMT", StringComparison.OrdinalIgnoreCase) == true ? "Business hours (GMT)" :
                     tz?.Contains("IST", StringComparison.OrdinalIgnoreCase) == true ? "Business hours (IST)" :
                     country?.Contains("Multi", StringComparison.OrdinalIgnoreCase) == true ? "Follow-the-sun" : "Business hours",
                     "Inferred from time zone and location."),

                "teamSize" =>
                    Placeholder(label, extra: $"Provide total FTE count by role (L1/L2/L3/SME) for the {procName} team."),

                "skillProfile" =>
                    ("Available",
                     $"Mixed skill profile typical for a {ptype?.ToLower()} process. Exact L1/L2/L3/SME breakdown to be confirmed with {owner}.",
                     "Inferred from process type."),

                "servicesScope" =>
                    string.IsNullOrWhiteSpace(bu)
                        ? Placeholder(label)
                        : ("Available",
                           $"{procName} services provided to {bu} customers. Detailed service catalogue to be confirmed with process owner.",
                           "Synthesised from intake."),

                "productPortfolio" =>
                    Placeholder(label, extra: $"Provide list of products and services covered under the {bu} tower."),

                "slaCommitments" =>
                    Placeholder(label, "process owner and the commercial team", $"Confirm SLA targets, CSI goals, KPI definitions, and governance clauses for {procName}."),

                "customerType" =>
                    ("Available", "Strategic", "Defaulted to Strategic — confirm with account team."),

                "renewalTimelines" =>
                    Placeholder(label, "the commercial and legal team", $"Review current term expiry date and auto-renewal provisions for {procName}."),

                "exitProjects" =>
                    ("Available", "None identified at time of DD. Confirm with account and transition leadership.", "Inferred — no exit data in intake."),

                "namedResources" =>
                    string.IsNullOrWhiteSpace(owner)
                        ? Placeholder(label, extra: "Provide names, roles, and tenure for all key contributors.")
                        : ("Available",
                           $"Process Owner: {owner} ({intake.ProcessOwnerEmail}). Additional named resources to be confirmed during DD walkthrough.",
                           "Sourced from intake process owner."),

                "keyPersonnel" =>
                    Placeholder(label, "the process owner and legal team", $"Review contractual named individuals, notice period obligations, and substitution procedures for {procName}."),

                "skillGaps" =>
                    ("Available",
                     $"Specific skill gaps for {procName} to be assessed during DD. Focus areas: {ptype?.ToLower()} process expertise, tooling knowledge, and domain-specific competencies.",
                     "Standard DD assessment prompt."),

                "seedTeam" =>
                    ("Available",
                     $"Seed team requirements for {procName} to be defined as part of transition planning. Recommend shadowing period of minimum 4 weeks.",
                     "Standard transition best practice."),

                "escalationHierarchy" =>
                    ("Available",
                     $"Standard escalation: L1 ({bu} team) → L2 ({owner}) → L3 (Management). Exact path to be validated during DD walkthrough.",
                     "Inferred from intake."),

                "orgHierarchy" =>
                    ("Available",
                     $"{procName} sits within {bu} under {owner}. Full org chart mapping (GCC/RCC/LCC) to be confirmed with HR/org design team.",
                     "Synthesised from intake."),

                "followSunModel" =>
                    (country?.Contains(",", StringComparison.Ordinal) == true
                     || country?.Contains("/", StringComparison.Ordinal) == true)
                        ? ("Available",
                           $"Yes — process operates across {location}. Handoff points and SLAs between locations to be documented during DD.",
                           "Inferred from multi-location intake.")
                        : ("Available",
                           $"No — process currently operates from a single location ({location}). Review if follow-the-sun is planned post-transition.",
                           "Inferred from intake location."),

                "regionalVariations" =>
                    ("Available",
                     $"Regional process variations (if any) for {procName} across {location} to be confirmed during DD walkthrough. Document any country-specific regulatory or tooling differences.",
                     "Standard DD prompt."),

                "deliveryModel" =>
                    ("Available",
                     string.IsNullOrWhiteSpace(bu) ? "Shared" :
                     bu.Contains("Shared", StringComparison.OrdinalIgnoreCase) ? "Shared Service" : "Dedicated",
                     "Inferred from business unit."),

                "geoDependencyRisks" =>
                    ("Available",
                     $"Single-location concentration risk if {procName} is currently run from one site in {location}. Confirm BCP and cross-training arrangements with {owner}.",
                     "Standard geo risk assessment."),

                "bcpPerGeo" =>
                    ("Available",
                     $"BCP arrangements for {procName} in {location} to be confirmed. Assess current RTO/RPO targets and identify gaps against TechM standards.",
                     "Standard BCP assessment prompt."),

                "inputSource" =>
                    ("Available",
                     "Customer / Internal — confirm exact trigger mechanism with process owner.",
                     "Inferred from process type."),

                "inputFormat" =>
                    ("Available",
                     ptype?.Contains("Auto", StringComparison.OrdinalIgnoreCase) == true
                         ? "API / E-bonding"
                         : "Manual / Email / Portal — confirm with process owner.",
                     "Inferred from process automation level."),

                "processFrequency" =>
                    ("Available",
                     vol > 100 ? "Real-time / Continuous" : vol > 10 ? "Daily" : "Weekly",
                     "Inferred from estimated daily volume."),

                "dataQualityIssues" =>
                    ("Available",
                     $"Data quality issues for {procName} to be assessed during DD. Common areas: input completeness, system data accuracy, and reporting consistency.",
                     "Standard data quality prompt."),

                "processOutput" =>
                    ("Available",
                     $"Output of {procName} is a processed transaction / resolved request delivered to the customer or internal stakeholder. Specific format and SLA to be confirmed.",
                     "Inferred from process name and type."),

                "outputRecipient" =>
                    string.IsNullOrWhiteSpace(bu)
                        ? Placeholder(label)
                        : ("Available", $"{bu} customer / internal stakeholder. Confirm exact recipient with process owner.", "Inferred from business unit."),

                "governanceForum" =>
                    ("Available",
                     $"{bu} Operations Review — Weekly/Monthly cadence. Confirm forum name, participants, and reporting format with {owner}.",
                     "Standard governance prompt."),

                "controlCheckpoints" =>
                    ("Available",
                     $"Control checkpoints for {procName} to be documented: input validation, SLA breach alerting, exception escalation, and audit log review.",
                     "Standard control framework."),

                "knownExceptions" =>
                    ("Available",
                     $"Common exceptions for {procName} include: incomplete input data, system unavailability, and escalation timeouts. Confirm full exception taxonomy with process owner.",
                     "Standard exception assessment."),

                "exceptionVolume" =>
                    ("Available",
                     vol > 0 ? $"Est. 5–10% of {vol} daily transactions. Confirm with process owner." : "Est. 5–10% — confirm with process owner.",
                     "Standard exception rate estimate."),

                "baselineCommentary" =>
                    ("Available",
                     $"Baseline data for {procName} is {(vol > 0 ? "partially available from intake (volume: " + vol + " transactions/day)" : "not yet captured")}. " +
                     $"Confirm whether figures are system-generated or manually estimated, and validate SLA measurement methodology with {owner} before finalising.",
                     "Standard baseline assessment prompt."),

                "toolLandscape" =>
                    Placeholder(label, "the process owner and technology team", $"Identify ITSM, CRM, billing, and reporting platforms used by the {bu} team. Confirm tool ownership, licensing, and portability."),

                "apiMaturity" =>
                    ("Available",
                     ptype?.Contains("Auto", StringComparison.OrdinalIgnoreCase) == true ? "Medium — automated process suggests API integration exists."
                     : "Low — confirm API availability with technology team.",
                     "Inferred from process automation level."),

                "automationOpps" =>
                    ("Available",
                     $"{procName} ({ptype}) has potential for further automation. Opportunities include: workflow automation, RPA for repetitive steps, and AI-assisted exception handling. Full assessment requires tooling analysis.",
                     "Standard automation assessment."),

                "aiUseCases" =>
                    ("Available",
                     $"Potential AI use cases for {procName}: intelligent routing, anomaly detection, predictive SLA breach alerting, and automated summarisation of process outcomes.",
                     "Standard AI opportunity scan."),

                "dataPlatform" =>
                    Placeholder(label, "the technology team", $"Assess use of EDH, data warehouses, or other analytics platforms for {procName}. Document data flows and integration points."),

                "dataQualityRating" =>
                    ("Available",
                     "Medium — specific data quality gaps to be assessed during DD walkthrough.",
                     "Standard data readiness assessment."),

                "dataOwnership" =>
                    string.IsNullOrWhiteSpace(owner)
                        ? Placeholder(label, extra: "Map each data source to a named owner and confirm governance responsibilities.")
                        : ("Available", $"{owner} ({bu}). Full data ownership map to be confirmed during DD.", "Inferred from process owner."),

                "resolverChanges" =>
                    ("Available",
                     $"Resolver group for {procName} will transition to TechM {bu} team. Existing client resolvers to be mapped to TechM role equivalents. Confirm exact changes with transition lead.",
                     "Standard WITO assessment."),

                "transitionRisk" =>
                    ("Available",
                     $"Key contractual performance risks during transition of {procName}: SLA breach during knowledge transfer period, key person dependency on {owner}, and tooling access delays. Mitigations to be agreed with client.",
                     "Standard WITO risk assessment."),

                "activeExits" =>
                    ("Available",
                     "No active customer exit projects identified at time of DD. Confirm with account team.",
                     "Inferred — no exit data in intake."),

                "assetBilling" =>
                    Placeholder(label, "the commercial team", $"Confirm asset billing scope, transition-period invoicing arrangements, and at-risk revenue implications for {procName}."),

                "exitObligations" =>
                    Placeholder(label, "the legal team", $"Confirm exit assistance duration, minimum notice periods, data portability, and transition support obligations for {procName}."),

                "ktRisk" =>
                    string.IsNullOrWhiteSpace(owner)
                        ? Placeholder(label, extra: "Identify key person dependencies and document KT plan including runbooks and shadowing schedule.")
                        : ("Available",
                           $"Key person dependency: {owner}. Critical knowledge concentration risk if this individual is not available during transition. Recommend structured KT plan with documented runbooks.",
                           "Inferred from intake process owner."),

                "reversibilityClauses" =>
                    Placeholder(label, "the legal and commercial team", $"Confirm reverse transition obligations, portability requirements, and cost implications for {procName}."),

                "confidenceScore" =>
                    ("Available", "3",
                     "Default score — to be updated by reviewer after completing the DD session."),

                "pendingDocuments" =>
                    intake.UploadedFileName != null
                        ? ("Available", "None — uploaded document received.", "Document uploaded with intake.")
                        : ("Available", $"Process documentation for {procName} — request from {owner}.", "No document attached to intake."),

                "systemsUsed" =>
                    Placeholder(label, "the process owner and technology team", $"Identify ITSM, CRM, billing, and reporting tools for {procName}, including access methods and integration dependencies."),

                "workInstructions" =>
                    Placeholder(label, extra: $"Document step-by-step system navigation, field entries, decision rules, and validation checks for each step of {procName}. Request existing SOPs or runbooks from {owner}."),

                // ── New S8 SOP template keys ─────────────────────────────────
                "approver" =>
                    string.IsNullOrWhiteSpace(owner)
                        ? Placeholder(label, extra: "Provide approver name and email.")
                        : ("Available", $"{owner} | {intake.ProcessOwnerEmail}", "Defaulted to process owner — update with formal approver."),

                "processDescription" =>
                    ("Available",
                     $"{procName} is a {ptype?.ToLower()} process operated by the {bu} team in {location}. " +
                     $"{desc} The process handles approximately {vol} transactions per day under the oversight of {owner}.",
                     "Synthesised from intake metadata."),

                "monthlyVolumes" =>
                    ("Available",
                     vol > 0 ? $"Estimated {vol} transactions per day (approx. {vol * 22} per month). Confirm exact monthly figures for the past 12 months with the process owner."
                             : "Monthly volume data to be confirmed with the process owner. Provide actuals for the past 12 months.",
                     "Inferred from intake daily volume estimate."),

                "hoursWeekday" =>
                    tz?.Contains("IST", StringComparison.OrdinalIgnoreCase) == true
                        ? ("Available", "09:00–18:00 IST", "Inferred from IST time zone.")
                        : tz?.Contains("GMT", StringComparison.OrdinalIgnoreCase) == true
                            ? ("Available", "09:00–17:30 GMT", "Inferred from GMT time zone.")
                            : ("Available", "09:00–18:00 local time", "Inferred from intake time zone."),

                "hoursWeekend" =>
                    ("Available", "Not Operational", "Standard business hours assumption — confirm with process owner."),

                "hoursHoliday" =>
                    ("Available", "On-call cover only — escalate to manager", "Standard holiday cover assumption — confirm with process owner."),

                "raciTask1" =>
                    ("Available", $"Receive and log incoming {procName} request", "Synthesised from process name."),

                "raciTask2" =>
                    ("Available", $"Process and validate {procName} transaction", "Synthesised from process type."),

                "raciTask3" =>
                    ("Available", $"Approve or escalate {procName} outcome", "Synthesised from process type."),

                "raciTask4" =>
                    ("Available", $"Close and report {procName} completion", "Synthesised from process name."),

                "raciRole1" =>
                    ("Available", $"{bu} Analyst", "Inferred from business unit."),

                "raciRole2" =>
                    string.IsNullOrWhiteSpace(owner)
                        ? ("Available", "Process Manager", "Standard role name.")
                        : ("Available", owner, "Sourced from intake process owner."),

                "raciRole3" =>
                    ("Available", $"{bu} Team Lead", "Inferred from business unit."),

                "raciRole4" =>
                    ("Available", $"Service Delivery Manager", "Standard governance role."),

                "sopAction" =>
                    ("Available",
                     $"Receive, validate, and process the {procName} transaction according to the standard procedure. Log all actions in the ticketing system.",
                     "Synthesised from process name and type."),

                "sopRole" =>
                    ("Available", $"{bu} Analyst", "Inferred from business unit."),

                "sopSystem" =>
                    ("Available", "Ticketing system / ERP — confirm with process owner", "Standard system placeholder."),

                "sopOutput" =>
                    ("Available", $"Processed and validated {procName} transaction record", "Synthesised from process name."),

                "sopDecision" =>
                    ("Available",
                     $"Is the {procName} request complete and within SLA? If Yes → proceed to completion. If No → escalate to Team Lead.",
                     "Synthesised from process name."),

                "sopDecisionOutput" =>
                    ("Available", "Proceed / Escalate to Team Lead", "Standard decision outcome."),

                "sopAutoStatus" =>
                    ptype?.Contains("Auto", StringComparison.OrdinalIgnoreCase) == true
                        ? ("Available", "Partially Automated", "Inferred from process type.")
                        : ("Available", "Manual", "Inferred from process type."),

                "sopOppRating" =>
                    ("Available", "Medium", "Standard automation opportunity assessment."),

                "sopAutoType" =>
                    ptype?.Contains("Auto", StringComparison.OrdinalIgnoreCase) == true
                        ? ("Available", "Workflow / Integration", "Inferred from process type.")
                        : ("Available", "RPA", "Highest-value automation type for manual processes."),

                "sopExtraStep" =>
                    ("Available", $"Complete documentation and notify stakeholders of {procName} closure", "Standard closing step."),

                "wiStepName" =>
                    ("Available", $"{procName} Processing", "Synthesised from process name."),

                "wiInstruction1a" =>
                    ("Available",
                     $"Log in to the ticketing system and navigate to the {procName} queue. Select the new request and open the intake form.",
                     "Synthesised from process name."),

                "wiInstruction1b" =>
                    ("Available",
                     $"Validate all mandatory fields in the request. If any field is blank or incorrect, return the request to the originator with a clear error note.",
                     "Standard validation instruction."),

                "wiErrorInstruction" =>
                    ("Available",
                     $"If a system error occurs, take a screenshot and raise an incident ticket immediately. Notify the Team Lead and log the error in the exception register. Do not proceed until the system is confirmed stable.",
                     "Standard error handling instruction."),

                "wiInstruction2a" =>
                    ("Available",
                     $"Open the {procName} processing screen and enter all required data fields. Cross-reference against the source document before saving.",
                     "Synthesised from process name."),

                "escalationTrigger" =>
                    ("Available", $"SLA breach risk — {procName} request approaching {(vol > 0 ? (vol / 10) + "-hour" : "agreed")} SLA threshold", "Inferred from process volume."),

                "escalationPath" =>
                    ("Available", $"Notify {bu} Team Lead via email and phone. Raise priority ticket in the ticketing system. CC Service Delivery Manager.", "Standard escalation path."),

                "escalationTimeframe" =>
                    ("Available", "Within 2 hours of breach detection", "Standard escalation SLA."),

                "escalationTarget" =>
                    ("Available", $"Resolve within 4 hours of escalation; confirm outcome with {bu} Team Lead", "Standard resolution target."),

                "exceptionType" =>
                    ("Available", $"Incomplete or invalid input data for {procName} request", "Most common exception type."),

                "exceptionHandling" =>
                    ("Available", "Return to originator with clear guidance. Log in exception register. Re-enter queue once corrected.", "Standard exception procedure."),

                "exceptionApproval" =>
                    ("Available", $"Yes — Team Lead approval required for any exceptions beyond standard handling", "Standard governance control."),

                "slaMetric" =>
                    ("Available", $"{procName} Processing Time", "Synthesised from process name."),

                "slaMeasurement" =>
                    ("Available", "Time from receipt of request to confirmed completion, measured in the ticketing system", "Standard SLA measurement."),

                "slaFrequency" =>
                    ("Available", vol > 100 ? "Daily" : "Weekly", "Inferred from transaction volume."),

                "slaTool" =>
                    ("Available", "Ticketing system / Service management platform — confirm with process owner", "Standard tooling."),

                "perfMetric" =>
                    ("Available", $"{procName} SLA Adherence (%)", "Synthesised from process name."),

                "perfActual" =>
                    ("Available", "Actual performance data to be confirmed with process owner — provide figures for the past 6 months", "Placeholder — confirm actuals."),

                "volumeTransaction" =>
                    ("Available",
                     vol > 0 ? $"{vol} {procName} transactions (daily average)"
                             : $"{procName} transaction type and volume to be confirmed with process owner",
                     "Inferred from intake volume data."),

                "volumeNote" =>
                    ("Available", "Confirm any peak periods (e.g. month-end, quarter-end) with process owner. Identify seasonal demand fluctuations.", "Standard volume note."),

                "volumeForecast" =>
                    ("Available",
                     vol > 0 ? $"Expected average: {vol} transactions/day ({vol * 22} per month). Review trend with process owner."
                             : "Forecast volume to be agreed with process owner based on historical trend.",
                     "Inferred from intake daily volume."),

                "regulation" =>
                    ("Available", "GDPR / Data Protection Act 2018 — applicable to any personal data processed in this workflow", "Standard regulatory framework."),

                "regObligation" =>
                    ("Available", $"Data minimisation, purpose limitation, and accuracy obligations apply to all {procName} records", "Standard GDPR obligation."),

                "regControl" =>
                    ("Available", $"Access controls, data retention policy, and audit logging implemented within the {procName} workflow", "Standard control description."),

                "regEvidence" =>
                    ("Available", "Data Protection Impact Assessment (DPIA) / Access control log / Retention schedule", "Standard evidence artefacts."),

                "techMFramework" =>
                    ("Available", "BARTOK-TCF-v1 (TechM BARTOK Control Framework — available on SharePoint)", "Standard TechM reference."),

                "trainingModule" =>
                    ("Available", $"{procName} Process Induction and Refresher", "Synthesised from process name."),

                "trainingDelivery" =>
                    ("Available", "On-the-job shadowing + e-learning module", "Standard TechM delivery method."),

                "trainingVerification" =>
                    ("Available", "Competency sign-off by Team Lead after 2-week shadowing period", "Standard verification approach."),

                "occRef" =>
                    Placeholder(label, "the OBI commercial team", "OCC references must be provided by OBI (Orange Business International) before this section can be completed."),

                "occObligation" =>
                    Placeholder(label, "the OBI commercial team", $"Orange Customer Contract obligations relevant to {procName} to be confirmed with OBI (Orange Business International)."),

                "occControl" =>
                    ("Available", $"{procName} includes controls aligned to standard TechM service delivery obligations. Specific OCC controls to be mapped once OCC schedules are received from OBI.", "Standard OCC control placeholder."),

                _ =>
                    Placeholder(label, extra: $"(Field key: {aiProp})")
            };
        }

        // Fields with no AI source — provide a reviewable placeholder
        return Placeholder(label, extra: "No automatic mapping found — please review and update.");
    }

    /// <summary>Formats city, country and optional site into a single location string.</summary>
    private static string FormatGeoLocation(IntakeRecord intake) =>
        $"{intake.City}, {intake.Country}" +
        (string.IsNullOrWhiteSpace(intake.SiteLocation) ? "" : $" ({intake.SiteLocation})");

    // ── PII scan helper ───────────────────────────────────────────────────────

    /// <summary>
    /// Scans <paramref name="userContent"/> for PII/SPII before it is forwarded to the LLM.
    /// <list type="bullet">
    ///   <item>If the tenant has PII scanning disabled → the original text is returned unchanged.</item>
    ///   <item>If PII is found and <c>BlockOnDetection = true</c> → throws <see cref="InvalidOperationException"/>.</item>
    ///   <item>If PII is found and <c>BlockOnDetection = false</c> → returns the redacted text (PII replaced with placeholders).</item>
    /// </list>
    /// </summary>
    private async Task<string> EnforcePiiPolicyAsync(string userContent, string callSite)
    {
        try
        {
            var result = await _piiScanner.ScanAsync(userContent);
            var tenantId = _tenantContext.GetCurrentTenantId();

            // Always log the PII scan result to the audit log.
            await _auditLog.LogPiiScanAsync(
                correlationId    : _currentCorrelationId,
                callSite         : callSite,
                tenantId         : tenantId,
                intakeRecordId   : _currentIntakeId,
                scanResult       : result);

            if (!result.HasPii)
                return userContent;

            var types = string.Join(", ", result.Findings.Select(f => f.EntityType).Distinct());
            _logger.LogWarning(
                "PII detected before LLM call ({CallSite}) — entity types: {Types}",
                callSite, types);

            if (result.ShouldBlock)
                throw new InvalidOperationException(
                    $"The request was blocked by the PII/SPII safeguard. " +
                    $"Detected sensitive data: {types}. " +
                    $"Remove personally identifiable information before submitting.");

            // Redact mode — accumulate findings for the caller, then forward redacted content.
            _lastAnalysisPiiFindings.AddRange(result.Findings);
            _logger.LogInformation(
                "PII scan ({CallSite}): redacted {Count} finding(s) before forwarding to LLM",
                callSite, result.Findings.Count);
            return result.RedactedText;
        }
        catch (InvalidOperationException)
        {
            throw; // re-throw block errors
        }
        catch (Exception ex)
        {
            // PII scanning failed due to an unexpected error.  Log it at WARNING level so
            // operators are aware, then forward the original content to prevent a total
            // outage.  If the tenant requires strict PII enforcement, configure
            // BlockOnDetection=true AND monitor the application logs for this warning.
            _logger.LogWarning(ex,
                "PII scan failed unexpectedly ({CallSite}) — forwarding original content. " +
                "Configure BlockOnDetection=true and monitor logs for repeated failures.",
                callSite);
            return userContent;
        }
    }

    /// <summary>
    /// Executes an HTTP POST to the LLM endpoint and writes an audit log entry for the call.
    /// Returns the raw JSON response body on success, or null on HTTP failure.
    /// </summary>
    private async Task<HttpResponseMessage> PostLlmAsync(
        HttpClient http,
        string requestUrl,
        object requestBody,
        string callSite)
    {
        var tenantId = _tenantContext.GetCurrentTenantId();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync(requestUrl, requestBody);
        }
        catch (Exception ex)
        {
            sw.Stop();
            await _auditLog.LogExternalCallAsync(
                correlationId  : _currentCorrelationId,
                callSite       : callSite,
                eventType      : "LlmCall",
                tenantId       : tenantId,
                intakeRecordId : _currentIntakeId,
                requestUrl     : requestUrl,
                httpStatusCode : null,
                durationMs     : sw.ElapsedMilliseconds,
                isMocked       : false,
                outcome        : "Error",
                errorMessage   : ex.Message);
            throw;
        }

        sw.Stop();
        await _auditLog.LogExternalCallAsync(
            correlationId  : _currentCorrelationId,
            callSite       : callSite,
            eventType      : "LlmCall",
            tenantId       : tenantId,
            intakeRecordId : _currentIntakeId,
            requestUrl     : requestUrl,
            httpStatusCode : (int)response.StatusCode,
            durationMs     : sw.ElapsedMilliseconds,
            isMocked       : false,
            outcome        : response.IsSuccessStatusCode ? "Success" : "Error");

        return response;
    }

    /// <summary>
    /// Logs a mocked LLM call (no real HTTP request was made).
    /// </summary>
    private async Task LogMockedCallAsync(string callSite)
    {
        var tenantId = _tenantContext.GetCurrentTenantId();
        await _auditLog.LogExternalCallAsync(
            correlationId  : _currentCorrelationId,
            callSite       : callSite,
            eventType      : "LlmCall",
            tenantId       : tenantId,
            intakeRecordId : _currentIntakeId,
            requestUrl     : null,
            httpStatusCode : null,
            durationMs     : 0,
            isMocked       : true,
            outcome        : "MockResponse");
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
        var hasDocument  = !string.IsNullOrWhiteSpace(intake.UploadedFileName);
        var hasOwner     = !string.IsNullOrWhiteSpace(intake.ProcessOwnerName);
        var hasCountry   = !string.IsNullOrWhiteSpace(intake.Country);
        var hasVolume    = intake.EstimatedVolumePerDay > 0;
        // Default mock value for stepsIdentified when AI is not configured
        const int mockStepsIdentified = 6;

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            _source = "mock",
            processName = intake.ProcessName,
            confidenceScore = 82,
            stepsIdentified = mockStepsIdentified,
            estimatedHandlingTimeMinutes = 12,
            complianceStatus = "Passed",
            automationPotential = "Medium",
            keyInsights = new[]
            {
                $"Process '{intake.ProcessName}' has {mockStepsIdentified} distinct steps identified",
                $"Located in {intake.City}, {intake.Country} — timezone considerations apply",
                $"Business unit '{intake.BusinessUnit}' shows standard process complexity",
                "BARTOK S8 SOP readiness check performed — see checkpoints and tasks for gap details"
            },
            recommendations = new[]
            {
                new { icon = "bulb", text = "Complete all BARTOK S8 SOP section tasks before scheduling the document review" },
                new { icon = "target", text = "Prioritise gathering monthly volumetrics data and SLA metrics from the process owner" },
                new { icon = "bar",   text = "Confirm automation opportunity rating and system names with the IT/Operations team" }
            },
            riskAreas = new[]
            {
                "Insufficient volumetrics data may delay SLA target-setting",
                "Escalation and exception-handling paths are not yet documented"
            },
            actionItems = new object[]
            {
                new { title = "Confirm Approver Details",
                      description = $"The BARTOK S8 SOP Document Control section requires an approver name and email address. Please confirm who will approve the SOP for '{intake.ProcessName}'.",
                      owner = hasOwner ? $"{intake.ProcessOwnerName}" : "Process Owner",
                      priority = "High",
                      bartokSection = "Document Control",
                      requiredInfo = "Approver full name; approver email address" },

                new { title = "Provide Monthly Transaction Volumes",
                      description = $"The BARTOK S8 SOP requires 12 months of transaction volume data, peak volume/period, and hours of operation (weekday/weekend/holiday) for '{intake.ProcessName}'. The intake currently shows {intake.EstimatedVolumePerDay} transactions/day but does not include monthly breakdowns.",
                      owner = hasOwner ? $"{intake.ProcessOwnerName}" : "Process Owner",
                      priority = "High",
                      bartokSection = "2. Process Overview",
                      requiredInfo = "Monthly transaction volumes for the last 12 months; peak volume and period (e.g. month-end); weekday hours of operation; weekend hours; public holiday cover arrangements; list of all systems used in the process" },

                new { title = "Define RACI Matrix",
                      description = $"Section 3 of the BARTOK S8 SOP requires a RACI matrix with 4 process-specific task names and 4 role titles for '{intake.ProcessName}'. Please provide task and role details so the RACI table can be populated.",
                      owner = hasOwner ? $"{intake.ProcessOwnerName}" : "Process Owner",
                      priority = "Medium",
                      bartokSection = "3. RACI",
                      requiredInfo = "4 task names specific to this process; 4 role titles; RACI assignments (Responsible/Accountable/Consulted/Informed) per cell" },

                new { title = "Document SOP Steps and Work Instructions",
                      description = $"Sections 4 and 5 of the BARTOK S8 SOP require detailed step-by-step actions, responsible roles, systems, decision points, automation assessment, and full work instructions for '{intake.ProcessName}'.",
                      owner = hasOwner ? $"{intake.ProcessOwnerName}" : "Process Owner",
                      priority = "High",
                      bartokSection = "4. SOP Steps",
                      requiredInfo = "Step-by-step actions with responsible role and system per step; expected output per step; decision points with Yes/No paths; automation status (Manual/Partially Automated/Fully Automated); automation opportunity rating (Low/Medium/High/Prime); automation type (RPA/AI/Workflow/Integration/N/A); detailed work instructions including system navigation and error-handling steps" },

                new { title = "Document Escalation Paths and Exception Handling",
                      description = $"Section 6 of the BARTOK S8 SOP requires escalation triggers, escalation paths, resolution timeframes, exception types, and handling approaches for '{intake.ProcessName}'.",
                      owner = hasOwner ? $"{intake.ProcessOwnerName}" : "Process Owner",
                      priority = "Medium",
                      bartokSection = "6. Escalation & Exceptions",
                      requiredInfo = "Escalation triggers; who to notify and how; resolution timeframe; exception types encountered; handling approach; approval requirements for exceptions" },

                new { title = "Provide SLA Metrics and Performance Data",
                      description = $"Section 7 of the BARTOK S8 SOP requires SLA metric names, measurement methods, reporting frequency, measurement tools, and actual vs target performance data for '{intake.ProcessName}'.",
                      owner = hasOwner ? $"{intake.ProcessOwnerName}" : "Process Owner",
                      priority = "Medium",
                      bartokSection = "7. SLAs & Performance",
                      requiredInfo = "SLA metric name(s); measurement method; reporting frequency; measurement tool; actual performance figures; target performance figures" },

                new { title = "Confirm Regulatory/Compliance References",
                      description = $"Section 9 of the BARTOK S8 SOP requires applicable regulations, obligations, controls, and evidence artefacts for '{intake.ProcessName}'.",
                      owner = hasOwner ? $"{intake.ProcessOwnerName}" : "Compliance Team",
                      priority = "Low",
                      bartokSection = "9. Regulatory & Compliance",
                      requiredInfo = "Applicable regulation(s) or standard(s); specific obligations; controls already in the process; evidence artefacts (documents/logs/reports); TechM Control Framework reference" },

                new { title = "Confirm Training Materials and OCC Obligations",
                      description = $"Sections 10 and 11 of the BARTOK S8 SOP require training module names, delivery methods, competency verification, and Orange Customer Contract (OCC) references for '{intake.ProcessName}'.",
                      owner = hasOwner ? $"{intake.ProcessOwnerName}" : "Training & Compliance Team",
                      priority = "Low",
                      bartokSection = "10. Training",
                      requiredInfo = "Training module name(s); delivery method (classroom/e-learning/on-the-job); competency verification approach; OCC reference numbers; OCC obligation descriptions; how this process addresses each OCC obligation" }
            },
            checkPoints = new object[]
            {
                new { label = "Document Control — Author & Approver",
                      status = hasOwner ? "Warning" : "Fail",
                      note   = hasOwner ? $"Process author set to {intake.ProcessOwnerName}. Approver not yet confirmed." : "Process owner not assigned — author and approver fields will be blank in the SOP." },

                new { label = "1. Purpose & Scope — Countries and Input Artefacts",
                      status = hasCountry ? "Warning" : "Fail",
                      note   = hasCountry ? $"Country '{intake.Country}' captured. Input artefact list needs confirmation." : "Country not specified — scope section cannot be completed." },

                new { label = "2. Process Overview — Volumes, Hours & Systems",
                      status = hasVolume ? "Warning" : "Fail",
                      note   = hasVolume ? $"Estimated volume {intake.EstimatedVolumePerDay}/day recorded. Monthly breakdown, peak period, hours of operation, and systems list are required." : "No volume data provided — process overview section incomplete." },

                new { label = "3. RACI — Tasks and Roles",
                      status = "Warning",
                      note   = "RACI tasks and role titles must be confirmed with the process owner before the RACI matrix can be populated." },

                new { label = "4. SOP Steps — Actions, Systems & Automation",
                      status = hasDocument ? "Warning" : "Fail",
                      note   = hasDocument ? $"Document '{intake.UploadedFileName}' uploaded. Detailed SOP steps, decision points, and automation assessment still need confirmation." : "No supporting document — SOP steps cannot be derived. Please upload the process document or walk-through notes." },

                new { label = "5. Work Instructions — Step-by-Step Detail",
                      status = hasDocument ? "Warning" : "Fail",
                      note   = hasDocument ? "Supporting document present. Detailed work instructions (navigation steps, field entries, error handling) need to be extracted or written." : "No document — work instructions cannot be drafted." },

                new { label = "6. Escalation & Exceptions",
                      status = "Fail",
                      note   = "No escalation triggers, paths, or exception-handling information found in the intake or document." },

                new { label = "7. SLAs & Performance",
                      status = "Fail",
                      note   = "No SLA metrics, measurement methods, or performance data found in the intake or document." },

                new { label = "8. Volumetrics",
                      status = hasVolume ? "Warning" : "Fail",
                      note   = hasVolume ? $"Intake records {intake.EstimatedVolumePerDay} transactions/day. Monthly volumes, peak notes, and forecast are required for the Volumetrics section." : "No volume information — Volumetrics section cannot be completed." },

                new { label = "9. Regulatory & Compliance",
                      status = "Warning",
                      note   = "No regulatory references found in the intake. Compliance team must confirm applicable regulations, controls, and evidence artefacts." },

                new { label = "10. Training & 11. OCC",
                      status = "Warning",
                      note   = "Training materials and OCC obligations have not been specified. These must be confirmed before the SOP is finalised." }
            },
            qualityScore = 68,
            summary = $"Initial BARTOK S8 SOP readiness assessment for '{intake.ProcessName}'. " +
                      $"The intake provides foundational data (process owner, business unit, location) but several SOP sections require additional information from the process owner. " +
                      $"Key gaps: monthly volumetrics, SLA metrics, SOP step detail, escalation paths, RACI assignments, and regulatory references. " +
                      $"Tasks have been created for each gap section — complete them before generating the BARTOK S8 SOP document."
        });
    }

    // ── RunQcCheckAsync ───────────────────────────────────────────────────────

    public async Task<string> RunQcCheckAsync(
        IntakeRecord intake, string? analysisJson, string? tasksSummary, string? documentText)
    {
        _currentCorrelationId = Guid.NewGuid().ToString("N")[..12];
        _currentIntakeId      = intake.Id;
        var (endpoint, apiKey, deployment, apiVersion, maxTokens, modelVersion) = await GetAiConfigAsync();

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
            IMPORTANT: You MUST base ALL of your evaluation exclusively on the information provided in
            this prompt — the intake data, AI analysis JSON, task completion evidence, and uploaded
            document content. Do NOT use any external knowledge from the internet or outside sources.
            Only assess the quality of the information explicitly present in the provided data.
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
                    new { role = "user",   content = await EnforcePiiPolicyAsync(userMessage, "RunQcCheck") }
                },
                max_tokens  = maxTokens,
                temperature = 0.3
            };

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("api-key", apiKey);
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var tenantId4Qc = _tenantContext.GetCurrentTenantId();
            var swQc = System.Diagnostics.Stopwatch.StartNew();
            var json     = JsonSerializer.Serialize(requestBody);
            var payload  = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await client.PostAsync(requestUrl, payload);
            swQc.Stop();
            var body     = await response.Content.ReadAsStringAsync();

            await _auditLog.LogExternalCallAsync(
                correlationId  : _currentCorrelationId,
                callSite       : "RunQcCheck",
                eventType      : "LlmCall",
                tenantId       : tenantId4Qc,
                intakeRecordId : _currentIntakeId,
                requestUrl     : requestUrl,
                httpStatusCode : (int)response.StatusCode,
                durationMs     : swQc.ElapsedMilliseconds,
                isMocked       : false,
                outcome        : response.IsSuccessStatusCode ? "Success" : "Error");

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

    // ─── GenerateSingleFieldAsync ─────────────────────────────────────────────

    public async Task<string> GenerateSingleFieldAsync(
        IntakeRecord intake,
        string fieldKey,
        string fieldLabel,
        string? userContext,
        string? analysisJson,
        string? artifactText)
    {
        _currentCorrelationId = Guid.NewGuid().ToString("N")[..12];
        _currentIntakeId      = intake.Id;
        var (endpoint, apiKey, deployment, apiVersion, maxTokens, modelVersion) = await GetAiConfigAsync();

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(deployment)
            || endpoint.Contains("YOUR_RESOURCE") || apiKey.Contains("YOUR_API_KEY")
            || deployment.Equals("YOUR_DEPLOYMENT_NAME", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Azure OpenAI not configured — cannot generate field '{FieldKey}' for intake {IntakeId}.", fieldKey, intake.IntakeId);
            return string.Empty;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
        {
            _logger.LogWarning("Azure OpenAI endpoint invalid — cannot generate field '{FieldKey}'.", fieldKey);
            return string.Empty;
        }

        var requestUrl = $"{endpoint.TrimEnd('/')}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";

        const string systemPrompt = """
            You are a BARTOK / Schedule 8 SOP documentation specialist at TechM.
            IMPORTANT: You MUST base ALL content exclusively on the information provided in this prompt —
            the intake metadata, any prior AI analysis, task artifact text, and the user's context notes.
            Do NOT use any external knowledge from the internet, Wikipedia, or outside sources. Only
            rephrase, elaborate, and structure information that is explicitly present in the provided data.
            Your task is to generate professional, document-ready content for a single SOP field.
            Return ONLY the field value as plain text — no JSON, no markdown headers, no extra commentary.
            The content should be concise (1-4 sentences) and suitable for direct insertion into a document.
            """;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Field to generate: {fieldLabel} (key: {fieldKey})");
        sb.AppendLine();
        sb.AppendLine("=== Intake Metadata ===");
        sb.AppendLine($"Process Name: {intake.ProcessName}");
        sb.AppendLine($"Description: {intake.Description}");
        sb.AppendLine($"Business Unit: {intake.BusinessUnit}");
        sb.AppendLine($"Department: {intake.Department}");
        sb.AppendLine($"Process Owner: {intake.ProcessOwnerName} ({intake.ProcessOwnerEmail})");
        sb.AppendLine($"Process Type: {intake.ProcessType}");
        sb.AppendLine($"Location: {intake.City}, {intake.Country}");
        sb.AppendLine($"Volume/day: {intake.EstimatedVolumePerDay}");
        sb.AppendLine($"Priority: {intake.Priority}");
        if (!string.IsNullOrWhiteSpace(userContext))
        {
            sb.AppendLine();
            sb.AppendLine("=== User Context / Additional Notes ===");
            sb.AppendLine(userContext);
        }
        if (!string.IsNullOrWhiteSpace(analysisJson))
        {
            var truncatedAnalysis = analysisJson.Length > MaxAnalysisJsonChars
                ? analysisJson[..MaxAnalysisJsonChars] + "\n[...truncated]"
                : analysisJson;
            sb.AppendLine();
            sb.AppendLine("=== Prior AI Analysis (JSON) ===");
            sb.AppendLine(truncatedAnalysis);
        }
        if (!string.IsNullOrWhiteSpace(artifactText))
        {
            var truncatedArtifact = artifactText.Length > MaxDocumentChars
                ? artifactText[..MaxDocumentChars] + "\n[...truncated]"
                : artifactText;
            sb.AppendLine();
            sb.AppendLine("=== Task Artifact Text ===");
            sb.AppendLine(truncatedArtifact);
        }

        var userMessage = sb.ToString();

        try
        {
            var requestBody = new
            {
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = await EnforcePiiPolicyAsync(userMessage, "GenerateSingleField") }
                },
                max_tokens  = Math.Min(maxTokens, 500),
                temperature = 0.4,
                top_p       = 1.0,
                model       = modelVersion
            };

            var http = _httpClientFactory.CreateClient();
            http.DefaultRequestHeaders.Add("api-key", apiKey);

            var response = await PostLlmAsync(http, requestUrl, requestBody, "GenerateSingleField");

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("Azure OpenAI returned {Status} for GenerateSingleField: {Body}",
                    (int)response.StatusCode, errorBody);
                return string.Empty;
            }

            var responseText = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(responseText);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()?.Trim() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GenerateSingleFieldAsync failed for field '{FieldKey}' on intake {IntakeId}.", fieldKey, intake.IntakeId);
            return string.Empty;
        }
    }

    // ─── GenerateSopFromTranscriptAsync ──────────────────────────────────────

    public async Task<string> GenerateSopFromTranscriptAsync(string transcript, IntakeRecord intake)
    {
        _currentCorrelationId = Guid.NewGuid().ToString("N")[..12];
        _currentIntakeId      = intake.Id;
        var (endpoint, apiKey, deployment, apiVersion, maxTokens, modelVersion) = await GetAiConfigAsync();

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
            IMPORTANT: You MUST base ALL content exclusively on the provided meeting transcript and the
            process context below. Do NOT use any external knowledge from the internet, Wikipedia, or
            outside sources. Only extract, rephrase, and structure information explicitly present in the
            transcript. For details not present in the transcript, add a [TO CONFIRM] placeholder.

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
                    new { role = "user",   content = await EnforcePiiPolicyAsync(userMessage, "GenerateSop") }
                },
                max_tokens  = Math.Max(maxTokens, 4000),
                temperature = 0.3
            };

            var client  = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("api-key", apiKey);
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var swSop = System.Diagnostics.Stopwatch.StartNew();
            var json     = JsonSerializer.Serialize(requestBody);
            var payload  = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await client.PostAsync(requestUrl, payload);
            swSop.Stop();
            var body     = await response.Content.ReadAsStringAsync();

            await _auditLog.LogExternalCallAsync(
                correlationId  : _currentCorrelationId,
                callSite       : "GenerateSop",
                eventType      : "LlmCall",
                tenantId       : _tenantContext.GetCurrentTenantId(),
                intakeRecordId : _currentIntakeId,
                requestUrl     : requestUrl,
                httpStatusCode : (int)response.StatusCode,
                durationMs     : swSop.ElapsedMilliseconds,
                isMocked       : false,
                outcome        : response.IsSuccessStatusCode ? "Success" : "Error");

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
        var (endpoint, apiKey, deployment, apiVersion, _, modelVersion) = await GetAiConfigAsync();

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

        var correlationId = Guid.NewGuid().ToString("N")[..12];
        var tenantId      = _tenantContext.GetCurrentTenantId();

        try
        {
            var http = _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            http.DefaultRequestHeaders.Add("api-key", apiKey);

            var sw       = System.Diagnostics.Stopwatch.StartNew();
            var response = await http.PostAsJsonAsync(requestUrl, requestBody);
            sw.Stop();
            int code = (int)response.StatusCode;

            await _auditLog.LogExternalCallAsync(
                correlationId  : correlationId,
                callSite       : "TestConnection",
                eventType      : "LlmCall",
                tenantId       : tenantId,
                intakeRecordId : null,
                requestUrl     : requestUrl,
                httpStatusCode : code,
                durationMs     : sw.ElapsedMilliseconds,
                isMocked       : false,
                outcome        : response.IsSuccessStatusCode ? "Success" : "Error");

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
            await _auditLog.LogExternalCallAsync(
                correlationId  : correlationId,
                callSite       : "TestConnection",
                eventType      : "LlmCall",
                tenantId       : tenantId,
                intakeRecordId : null,
                requestUrl     : requestUrl,
                httpStatusCode : null,
                durationMs     : 0,
                isMocked       : false,
                outcome        : "Error",
                errorMessage   : "Request timed out (15 s).");
            return (false, 0, "Request timed out (15 s). Verify the endpoint URL is reachable.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure OpenAI connection test threw an exception.");
            await _auditLog.LogExternalCallAsync(
                correlationId  : correlationId,
                callSite       : "TestConnection",
                eventType      : "LlmCall",
                tenantId       : tenantId,
                intakeRecordId : null,
                requestUrl     : requestUrl,
                httpStatusCode : null,
                durationMs     : 0,
                isMocked       : false,
                outcome        : "Error",
                errorMessage   : ex.Message);
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

    /// <summary>
    /// Strips leading/trailing markdown code fences (e.g. ```json ... ```) that the AI
    /// sometimes adds despite being instructed not to, so the result can be parsed as JSON.
    /// </summary>
    private static string StripMarkdownFences(string content)
    {
        var s = content.Trim();

        // Remove opening fence: ```json or ``` (optionally with language hint)
        if (s.StartsWith("```", StringComparison.Ordinal))
        {
            var newline = s.IndexOf('\n');
            if (newline >= 0)
                s = s[(newline + 1)..].TrimStart();
        }

        // Remove closing fence
        if (s.EndsWith("```", StringComparison.Ordinal))
            s = s[..^3].TrimEnd();

        return s;
    }

    // ── GenerateDescriptionAsync ───────────────────────────────────────────────
    /// <summary>
    /// Expands brief pointers into a detailed, professional process description.
    /// Returns the generated text, or an empty string when AI is not configured / unavailable.
    /// </summary>
    public async Task<string> GenerateDescriptionAsync(string processName, string pointers)
    {
        _currentCorrelationId = Guid.NewGuid().ToString("N")[..12];
        _currentIntakeId      = null; // not tied to a specific intake
        var (endpoint, apiKey, deployment, apiVersion, _, modelVersion) = await GetAiConfigAsync();

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
            IMPORTANT: You MUST base your description exclusively on the process name and key points
            provided by the user. Do NOT use any external knowledge from the internet, Wikipedia, or
            outside sources. Only expand, rephrase, and elaborate on the information explicitly given.
            Expand the provided key points into a comprehensive, professional process description (150-300 words).
            The description should clearly explain what the process does, who is involved, the key steps
            and inputs/outputs, and the business value it delivers, based solely on the provided information.
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
                    new { role = "user",   content = await EnforcePiiPolicyAsync(userMessage, "GenerateDescription") }
                },
                max_tokens  = 600,
                temperature = 0.7,
                top_p       = 1.0,
                model       = modelVersion
            };

            var http = _httpClientFactory.CreateClient();
            http.DefaultRequestHeaders.Add("api-key", apiKey);

            var response = await PostLlmAsync(http, requestUrl, requestBody, "GenerateDescription");

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
