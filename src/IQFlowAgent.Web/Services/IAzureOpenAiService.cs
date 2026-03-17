using IQFlowAgent.Web.Models;

namespace IQFlowAgent.Web.Services;

public interface IAzureOpenAiService
{
    Task<string> AnalyzeIntakeAsync(IntakeRecord intake, string? documentText);

    /// <summary>
    /// Returns the PII/SPII findings that were detected (and redacted) during the most
    /// recent call to <see cref="AnalyzeIntakeAsync"/>.  The list is empty when PII
    /// scanning is disabled or no PII was found.  A new <see cref="IAzureOpenAiService"/>
    /// scope resets this list to empty.
    /// </summary>
    IReadOnlyList<PiiFinding> GetLastPiiFindings();

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
    /// Generates content for a single report field using AI, grounded in the provided user context
    /// and the intake data/document content. Returns the generated text or an empty string on failure.
    /// </summary>
    Task<string> GenerateSingleFieldAsync(
        IntakeRecord intake,
        string fieldKey,
        string fieldLabel,
        string? userContext,
        string? analysisJson,
        string? artifactText);

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

    /// <summary>
    /// Expands brief user-supplied pointers into a detailed, professional process description.
    /// Returns the description as plain text, or an empty string if AI is unavailable.
    /// </summary>
    Task<string> GenerateDescriptionAsync(string processName, string pointers);

    /// <summary>
    /// Analyzes a single BARTOK section using only the artifact text gathered for that
    /// section's checkpoint tasks (e.g. the Volume task's Excel → Volume section).
    /// Runs at low temperature (0.1) for maximum extraction fidelity.
    /// <para>
    /// <paramref name="taskArtifactText"/> is the PRIMARY source — extracted from files
    /// attached when the section's task(s) were completed (Excel volume tables, Word SLA
    /// documents, etc.).  <paramref name="globalDocText"/> is used as background context.
    /// </para>
    /// Returns a dictionary of fieldKey → fillValue (only fields with real values
    /// are included; empty dict when AI is not configured).
    /// </summary>
    Task<Dictionary<string, string>> AnalyzeSectionFieldsAsync(
        IntakeRecord intake,
        string sectionName,
        IList<FieldDefinition> sectionFields,
        string? taskArtifactText,
        string? globalDocText,
        string? analysisJson);
}
