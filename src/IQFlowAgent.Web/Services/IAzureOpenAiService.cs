using IQFlowAgent.Web.Models;

namespace IQFlowAgent.Web.Services;

public interface IAzureOpenAiService
{
    Task<string> AnalyzeIntakeAsync(IntakeRecord intake, string? documentText);
}
