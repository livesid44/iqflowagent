using IQFlowAgent.Web.Models;

namespace IQFlowAgent.Web.Services;

public interface IAzureOpenAiService
{
    Task<string> AnalyzeIntakeAsync(IntakeRecord intake, string? documentText);

    /// <summary>
    /// Verifies whether all tasks for an intake have sufficient closure evidence.
    /// Returns structured JSON with per-task verdicts and an overall canCloseIntake flag.
    /// </summary>
    Task<string> VerifyIntakeClosureAsync(IntakeRecord intake, IList<IntakeTask> tasks, string? aggregatedArtifactText);

    /// <summary>
    /// Analyzes which BARTOK DD template fields can be filled from the available
    /// intake data, AI analysis JSON, and task artifact text.
    /// Returns structured JSON: { "fields": [{ "key", "status", "fillValue", "notes" }] }
    /// </summary>
    Task<string> AnalyzeReportFieldsAsync(
        IntakeRecord intake,
        string fieldDefinitionsJson,
        string? analysisJson,
        string? artifactText);

    /// <summary>
    /// Runs a QC check against the closed intake, all task documents, and any final report.
    /// Returns structured JSON with per-parameter scores and an overall score 0–100.
    /// </summary>
    Task<string> RunQcCheckAsync(
        IntakeRecord intake,
        string? analysisJson,
        string? tasksSummary,
        string? documentText);

    /// <summary>
    /// Generates a structured SOP / training document from a meeting transcript.
    /// Returns the SOP as a markdown string.
    /// </summary>
    Task<string> GenerateSopFromTranscriptAsync(string transcript, IntakeRecord intake);

    /// <summary>
    /// Sends a lightweight test prompt to the configured Azure OpenAI endpoint.
    /// Returns (success, httpStatusCode, message).
    /// </summary>
    Task<(bool success, int statusCode, string message)> TestConnectionAsync();
}
