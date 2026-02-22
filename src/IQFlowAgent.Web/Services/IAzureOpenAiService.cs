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
}
