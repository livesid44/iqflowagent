namespace IQFlowAgent.Web.ViewModels;

public class DashboardViewModel
{
    // ── Intake KPIs (global, unfiltered) ────────────────────────────
    public int TotalIntakes     { get; set; }
    public int ActiveIntakes    { get; set; }   // Submitted | Analyzing | Complete (non-Draft, non-Closed, non-Error)
    public int ClosedIntakes    { get; set; }   // Closed
    public int DraftIntakes     { get; set; }   // Draft
    public int ErrorIntakes     { get; set; }   // Error

    // ── Task KPIs (global, unfiltered) ──────────────────────────────
    public int TotalTasks       { get; set; }
    public int OpenTasks        { get; set; }
    public int InProgressTasks  { get; set; }
    public int CompletedTasks   { get; set; }
    public int CancelledTasks   { get; set; }

    // ── Per-intake detail rows (filtered) ───────────────────────────
    public List<IntakeSummaryRow> IntakeRows { get; set; } = [];
}

public class IntakeSummaryRow
{
    public int      Id           { get; set; }
    public string   IntakeId     { get; set; } = string.Empty;
    public string   ProcessName  { get; set; } = string.Empty;
    public string   BusinessUnit { get; set; } = string.Empty;
    public string   Country      { get; set; } = string.Empty;
    public string   Priority     { get; set; } = string.Empty;
    public string   Status       { get; set; } = string.Empty;
    public DateTime CreatedAt    { get; set; }
    public bool     HasFinalReport { get; set; }

    public int TotalTasks       { get; set; }
    public int OpenTasks        { get; set; }
    public int InProgressTasks  { get; set; }
    public int CompletedTasks   { get; set; }
    public int CancelledTasks   { get; set; }

    // Derived
    public int ClosedTasks      => CompletedTasks + CancelledTasks;
    public int ProgressPercent  => TotalTasks == 0 ? 0
        : (int)Math.Round(ClosedTasks * 100.0 / TotalTasks);
}
