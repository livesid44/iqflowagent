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
        IList<string>? artefactFileNames = null,
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
