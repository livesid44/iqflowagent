using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace IQFlowAgent.Web.Services;

public class BlobStorageService : IBlobStorageService
{
    private const string PlaceholderConnection = "YOUR_STORAGE_CONNECTION_STRING";

    // Plain-text extensions whose content is read and sent to AI analysis
    private static readonly HashSet<string> TextExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".txt", ".csv", ".json", ".xml", ".md" };

    private readonly IConfiguration _config;
    private readonly ILogger<BlobStorageService> _logger;

    public BlobStorageService(IConfiguration config, ILogger<BlobStorageService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public bool IsConfigured
    {
        get
        {
            var conn = _config["AzureStorage:ConnectionString"];
            return !string.IsNullOrWhiteSpace(conn)
                && !conn.Equals(PlaceholderConnection, StringComparison.OrdinalIgnoreCase);
        }
    }

    public async Task<string> UploadAsync(Stream content, string blobName, string contentType)
    {
        var containerClient = await GetContainerClientAsync();

        var blobClient = containerClient.GetBlobClient(blobName);
        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        };

        content.Position = 0;
        await blobClient.UploadAsync(content, uploadOptions);

        _logger.LogInformation("Uploaded blob {BlobName} to container {Container}",
            blobName, containerClient.Name);

        return blobClient.Uri.ToString();
    }

    public async Task<string?> DownloadTextAsync(string blobUrl)
    {
        var ext = Path.GetExtension(new Uri(blobUrl).LocalPath);
        if (!TextExtensions.Contains(ext))
            return null;

        try
        {
            var conn = _config["AzureStorage:ConnectionString"]!;
            var containerName = _config["AzureStorage:ContainerName"] ?? "intakes";
            var blobName = Path.GetFileName(new Uri(blobUrl).LocalPath);
            var credentialedClient = new BlobClient(conn, containerName, blobName);

            var response = await credentialedClient.DownloadContentAsync();
            return response.Value.Content.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not download text content from blob {BlobUrl}", blobUrl);
            return null;
        }
    }

    public async Task DeleteAsync(string blobUrl)
    {
        try
        {
            var conn = _config["AzureStorage:ConnectionString"]!;
            var containerName = _config["AzureStorage:ContainerName"] ?? "intakes";
            var blobName = Path.GetFileName(new Uri(blobUrl).LocalPath);
            var blobClient = new BlobClient(conn, containerName, blobName);
            await blobClient.DeleteIfExistsAsync();
            _logger.LogInformation("Deleted blob {BlobName}", blobName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete blob at {BlobUrl}", blobUrl);
        }
    }

    // -------------------------------------------------------------------------

    private async Task<BlobContainerClient> GetContainerClientAsync()
    {
        var conn = _config["AzureStorage:ConnectionString"]!;
        var containerName = _config["AzureStorage:ContainerName"] ?? "intakes";

        var serviceClient = new BlobServiceClient(conn);
        var containerClient = serviceClient.GetBlobContainerClient(containerName);

        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);
        return containerClient;
    }
}
