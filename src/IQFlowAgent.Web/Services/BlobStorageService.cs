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
    private readonly ITenantContextService _tenantContext;

    public BlobStorageService(IConfiguration config, ILogger<BlobStorageService> logger, ITenantContextService tenantContext)
    {
        _config = config;
        _logger = logger;
        _tenantContext = tenantContext;
    }

    public async Task<bool> IsConfiguredAsync()
    {
        var (conn, _) = await GetStorageConfigAsync();
        return !string.IsNullOrWhiteSpace(conn)
            && !conn.Equals(PlaceholderConnection, StringComparison.OrdinalIgnoreCase);
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
            var (conn, containerName) = await GetStorageConfigAsync();
            var blobName = Path.GetFileName(new Uri(blobUrl).LocalPath);
            var credentialedClient = new BlobClient(conn!, containerName, blobName);

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
            var (conn, containerName) = await GetStorageConfigAsync();
            var blobName = Path.GetFileName(new Uri(blobUrl).LocalPath);
            var blobClient = new BlobClient(conn!, containerName, blobName);
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
        var (conn, containerName) = await GetStorageConfigAsync();

        var serviceClient = new BlobServiceClient(conn!);
        var containerClient = serviceClient.GetBlobContainerClient(containerName);

        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);
        return containerClient;
    }

    private async Task<(string? connectionString, string containerName)> GetStorageConfigAsync()
    {
        var tenantSettings = await _tenantContext.GetCurrentTenantAiSettingsAsync();
        if (tenantSettings != null
            && !string.IsNullOrWhiteSpace(tenantSettings.AzureStorageConnectionString)
            && !tenantSettings.AzureStorageConnectionString.Equals(PlaceholderConnection, StringComparison.OrdinalIgnoreCase))
        {
            return (tenantSettings.AzureStorageConnectionString, tenantSettings.AzureStorageContainerName);
        }
        var conn = _config["AzureStorage:ConnectionString"];
        var container = _config["AzureStorage:ContainerName"] ?? "intakes";
        return (conn, container);
    }
}
