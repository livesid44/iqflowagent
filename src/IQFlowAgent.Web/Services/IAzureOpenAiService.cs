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
}
