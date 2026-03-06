namespace IQFlowAgent.Web.Services;

public interface IBlobStorageService
{
    /// <summary>Returns true when Azure Blob Storage is fully configured.</summary>
    Task<bool> IsConfiguredAsync();

    /// <summary>
    /// Uploads a stream to blob storage and returns the blob URL.
    /// </summary>
    Task<string> UploadAsync(Stream content, string blobName, string contentType);

    /// <summary>
    /// Downloads the blob at <paramref name="blobUrl"/> and returns its text content,
    /// or <c>null</c> if the blob is not a parseable text format.
    /// </summary>
    Task<string?> DownloadTextAsync(string blobUrl);

    /// <summary>Deletes the blob at <paramref name="blobUrl"/> if it exists.</summary>
    Task DeleteAsync(string blobUrl);

    /// <summary>
    /// Generates a short-lived SAS download URL for the given blob URL.
    /// Returns the original URL unchanged when storage is not configured or the
    /// connection string does not contain a shared key (e.g. SAS-only connections).
    /// </summary>
    Task<string> GenerateSasDownloadUrlAsync(string blobUrl, TimeSpan? expiry = null);
}
