using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;

namespace IQFlowAgent.Web.Services;

public class BlobStorageService : IBlobStorageService
{
    private const string PlaceholderConnection = "YOUR_STORAGE_CONNECTION_STRING";

    /// <summary>Default lifetime of generated SAS download tokens.</summary>
    private static readonly TimeSpan DefaultSasExpiry = TimeSpan.FromHours(1);

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

    public async Task<string> GenerateSasDownloadUrlAsync(string blobUrl, TimeSpan? expiry = null)
    {
        try
        {
            var (conn, containerName) = await GetStorageConfigAsync();
            if (string.IsNullOrWhiteSpace(conn))
                return blobUrl;

            // Parse the blob name from the URL using Uri.Segments for reliability.
            // Azure Blob Storage URLs have the form:
            //   https://{account}.blob.core.windows.net/{container}/{blob...}
            // Segments[0] = "/"  Segments[1] = "{container}/"  Segments[2..] = blob path parts
            var blobUri = new Uri(blobUrl);
            if (blobUri.Segments.Length < 3)
            {
                _logger.LogWarning("GenerateSasDownloadUrlAsync: unexpected URL structure '{Url}' — returning bare URL.", blobUrl);
                return blobUrl;
            }

            // Combine all segments after the container (index 2 onwards) to get the blob name,
            // then URL-decode to handle any percent-encoded characters.
            var blobName = Uri.UnescapeDataString(
                string.Concat(blobUri.Segments[2..]).TrimStart('/'));

            // Build a BlobClient using the connection string (includes the StorageSharedKeyCredential)
            var blobClient = new BlobClient(conn, containerName, blobName);

            // GenerateSasUri requires the client to have been created with a StorageSharedKeyCredential.
            // Connection strings that contain AccountName + AccountKey satisfy this requirement.
            if (!blobClient.CanGenerateSasUri)
            {
                _logger.LogWarning("BlobStorageService: cannot generate SAS URI — connection string may not contain a shared key. Returning bare URL.");
                return blobUrl;
            }

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = containerName,
                BlobName          = blobName,
                Resource          = "b",
                ExpiresOn         = DateTimeOffset.UtcNow.Add(expiry ?? DefaultSasExpiry)
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            var sasUri = blobClient.GenerateSasUri(sasBuilder);
            return sasUri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GenerateSasDownloadUrlAsync failed for {BlobUrl} — returning bare URL.", blobUrl);
            return blobUrl;
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
