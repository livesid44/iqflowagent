using IQFlowAgent.Web.Models;

namespace IQFlowAgent.Web.Services;

public interface IDocxReportService
{
    /// <summary>Returns the ordered list of BARTOK DD template field definitions.</summary>
    IReadOnlyList<FieldDefinition> GetFieldDefinitions();

    /// <summary>
    /// Generates a filled copy of the BARTOK DD template.
    /// Returns the .docx bytes ready to stream or upload.
    /// </summary>
    Task<byte[]> GenerateReportAsync(
        IntakeRecord intake,
        IList<ReportFieldStatus> fieldStatuses,
        string templatePath,
        IList<ArtefactFile>? artefactFiles = null,
        IList<(string FileName, byte[] Data)>? processFlowImages = null);
}

/// <summary>Static definition of one BARTOK template field.</summary>
public sealed record FieldDefinition(
    string Key,
    string Section,
    string Label,
    string TemplatePlaceholder,
    /// <summary>
    /// When not null, this intake property name is auto-mapped to the field.
    /// Use "AI" when the value should come from the AI analysis JSON.
    /// Use "" (empty) when the user must supply the value manually.
    /// </summary>
    string? AutoSource);

/// <summary>
/// Metadata for a single file to be listed in the 1.2 Artefacts table of the BARTOK DD report.
/// </summary>
public sealed record ArtefactFile(
    /// <summary>File name as uploaded.</summary>
    string FileName,
    /// <summary>Display name of the person who uploaded the file, or null if unknown.</summary>
    string? UploadedBy,
    /// <summary>Source identifier, e.g. "Task #5" or "Intake #12".</summary>
    string SourceId);

