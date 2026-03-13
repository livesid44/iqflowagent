namespace IQFlowAgent.Web.Models;

/// <summary>
/// Per-tenant configuration controlling which intake form fields are visible
/// and whether they are mandatory.
/// </summary>
public class IntakeFieldConfig
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;

    /// <summary>Stable key that matches the IntakeViewModel property name (e.g. "ProcessName").</summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>Human-readable label displayed in the configuration UI.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Logical grouping shown in the configuration UI (e.g. "Process Information").</summary>
    public string SectionName { get; set; } = string.Empty;

    /// <summary>When false the field is hidden on the intake Create/Edit form.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>When true and IsVisible is true the field is required on submit.</summary>
    public bool IsMandatory { get; set; } = false;

    /// <summary>Ordering within the section.</summary>
    public int DisplayOrder { get; set; } = 0;

    // ── Well-known field name constants ─────────────────────────────────────
    public const string FProcessName           = "ProcessName";
    public const string FDescription           = "Description";
    public const string FProcessType           = "ProcessType";
    public const string FPriority              = "Priority";
    public const string FEstimatedVolumePerDay = "EstimatedVolumePerDay";
    public const string FBusinessUnit          = "BusinessUnit";
    public const string FDepartment            = "Department";
    public const string FLob                   = "Lob";
    public const string FSdcLots               = "SdcLots";
    public const string FProcessOwnerName      = "ProcessOwnerName";
    public const string FProcessOwnerEmail     = "ProcessOwnerEmail";
    public const string FCountry               = "Country";
    public const string FCity                  = "City";
    public const string FSiteLocation          = "SiteLocation";
    public const string FTimeZone              = "TimeZone";
    public const string FDocument              = "Document";

    /// <summary>
    /// Returns the canonical set of field definitions used when provisioning
    /// a fresh set of configs for a tenant.
    /// </summary>
    public static readonly (string FieldName, string DisplayName, string SectionName, bool IsMandatory, int DisplayOrder)[] DefaultFields =
    [
        (FProcessName,           "Process Name",           "Process Information",      true,  1),
        (FDescription,           "Description",            "Process Information",      true,  2),
        (FProcessType,           "Process Type",           "Process Information",      false, 3),
        (FPriority,              "Priority",               "Process Information",      false, 4),
        (FEstimatedVolumePerDay, "Est. Volume / Day",      "Process Information",      false, 5),
        (FBusinessUnit,          "Business Unit",          "Ownership & Organisation", false, 6),
        (FDepartment,            "Department",             "Ownership & Organisation", false, 7),
        (FLob,                   "Line of Business (LOB)", "Ownership & Organisation", false, 8),
        (FSdcLots,               "Lots or SDC",            "Ownership & Organisation", false, 9),
        (FProcessOwnerName,      "Process Owner Name",     "Ownership & Organisation", false, 10),
        (FProcessOwnerEmail,     "Process Owner Email",    "Ownership & Organisation", false, 11),
        (FCountry,               "Country",                "Location",                 false, 12),
        (FCity,                  "City",                   "Location",                 false, 13),
        (FSiteLocation,          "Site / Office Location", "Location",                 false, 14),
        (FTimeZone,              "Time Zone",              "Location",                 false, 15),
        (FDocument,              "Document Upload",        "Documents",                false, 16),
    ];
}
