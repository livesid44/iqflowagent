namespace IQFlowAgent.Web.Services;

public interface IAzureSpeechService
{
    /// <summary>
    /// Returns true when the tenant has Speech region + API key configured.
    /// </summary>
    Task<bool> IsConfiguredAsync(int tenantId);

    /// <summary>
    /// Transcribes an audio or video file using Azure Batch Transcription REST API.
    /// Polls until the job is done (or times out after ~5 min) and returns the full transcript text.
    /// Falls back to a mock transcript when Speech is not configured.
    /// </summary>
    /// <param name="audioBlobUrl">Public/SAS URL of the audio/video file in Azure Blob Storage, OR a local file path.</param>
    /// <param name="tenantId">Tenant whose Speech credentials to use.</param>
    /// <param name="fileName">Original file name (used for language hint and logging).</param>
    Task<string> TranscribeAsync(string audioBlobUrl, int tenantId, string fileName);
}
