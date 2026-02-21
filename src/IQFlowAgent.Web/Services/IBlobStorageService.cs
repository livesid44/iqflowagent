namespace IQFlowAgent.Web.Services;

public interface IBlobStorageService
{
    /// <summary>Returns true when Azure Blob Storage is fully configured.</summary>
    bool IsConfigured { get; }

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
}
