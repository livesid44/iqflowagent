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
    private readonly IAuditLogService _auditLog;

    public BlobStorageService(IConfiguration config, ILogger<BlobStorageService> logger,
        ITenantContextService tenantContext, IAuditLogService auditLog)
    {
        _config        = config;
        _logger        = logger;
        _tenantContext = tenantContext;
        _auditLog      = auditLog;
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
        var correlationId = Guid.NewGuid().ToString("N")[..12];
        var tenantId = _tenantContext.GetCurrentTenantId();
        var sw       = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await blobClient.UploadAsync(content, uploadOptions);
            sw.Stop();

            _logger.LogInformation("Uploaded blob {BlobName} to container {Container}",
                blobName, containerClient.Name);

            await _auditLog.LogExternalCallAsync(
                correlationId  : correlationId,
                callSite       : "BlobUpload",
                eventType      : "BlobStorage",
                tenantId       : tenantId,
                intakeRecordId : null,
                requestUrl     : blobClient.Uri.ToString(),
                httpStatusCode : 201,
                durationMs     : sw.ElapsedMilliseconds,
                isMocked       : false,
                outcome        : "Success");

            return blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
            sw.Stop();
            await _auditLog.LogExternalCallAsync(
                correlationId  : correlationId,
                callSite       : "BlobUpload",
                eventType      : "BlobStorage",
                tenantId       : tenantId,
                intakeRecordId : null,
                requestUrl     : blobClient.Uri.ToString(),
                httpStatusCode : null,
                durationMs     : sw.ElapsedMilliseconds,
                isMocked       : false,
                outcome        : "Error",
                errorMessage   : ex.Message);
            throw;
        }
    }

    public async Task<string?> DownloadTextAsync(string blobUrl)
    {
        var ext = Path.GetExtension(new Uri(blobUrl).LocalPath);
        if (!TextExtensions.Contains(ext))
            return null;

        var tenantId      = _tenantContext.GetCurrentTenantId();
        var correlationId = Guid.NewGuid().ToString("N")[..12];
        var sw            = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var (conn, containerName) = await GetStorageConfigAsync();
            var blobName = Path.GetFileName(new Uri(blobUrl).LocalPath);
            var credentialedClient = new BlobClient(conn!, containerName, blobName);

            var response = await credentialedClient.DownloadContentAsync();
            sw.Stop();

            await _auditLog.LogExternalCallAsync(
                correlationId  : correlationId,
                callSite       : "BlobDownload",
                eventType      : "BlobStorage",
                tenantId       : tenantId,
                intakeRecordId : null,
                requestUrl     : blobUrl,
                httpStatusCode : 200,
                durationMs     : sw.ElapsedMilliseconds,
                isMocked       : false,
                outcome        : "Success");

            return response.Value.Content.ToString();
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Could not download text content from blob {BlobUrl}", blobUrl);

            await _auditLog.LogExternalCallAsync(
                correlationId  : correlationId,
                callSite       : "BlobDownload",
                eventType      : "BlobStorage",
                tenantId       : tenantId,
                intakeRecordId : null,
                requestUrl     : blobUrl,
                httpStatusCode : null,
                durationMs     : sw.ElapsedMilliseconds,
                isMocked       : false,
                outcome        : "Error",
                errorMessage   : ex.Message);

            return null;
        }
    }

    public async Task<byte[]?> DownloadBytesAsync(string blobUrl)
    {
        var tenantId      = _tenantContext.GetCurrentTenantId();
        var correlationId = Guid.NewGuid().ToString("N")[..12];
        var sw            = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var (conn, containerName) = await GetStorageConfigAsync();
            var blobName = Path.GetFileName(new Uri(blobUrl).LocalPath);
            var credentialedClient = new BlobClient(conn!, containerName, blobName);

            var response = await credentialedClient.DownloadContentAsync();
            sw.Stop();

            await _auditLog.LogExternalCallAsync(
                correlationId  : correlationId,
                callSite       : "BlobDownloadBytes",
                eventType      : "BlobStorage",
                tenantId       : tenantId,
                intakeRecordId : null,
                requestUrl     : blobUrl,
                httpStatusCode : 200,
                durationMs     : sw.ElapsedMilliseconds,
                isMocked       : false,
                outcome        : "Success");

            return response.Value.Content.ToArray();
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Could not download binary content from blob {BlobUrl}", blobUrl);

            await _auditLog.LogExternalCallAsync(
                correlationId  : correlationId,
                callSite       : "BlobDownloadBytes",
                eventType      : "BlobStorage",
                tenantId       : tenantId,
                intakeRecordId : null,
                requestUrl     : blobUrl,
                httpStatusCode : null,
                durationMs     : sw.ElapsedMilliseconds,
                isMocked       : false,
                outcome        : "Error",
                errorMessage   : ex.Message);

            return null;
        }
    }

    public async Task DeleteAsync(string blobUrl)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..12];
        var tenantId      = _tenantContext.GetCurrentTenantId();
        var sw            = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var (conn, containerName) = await GetStorageConfigAsync();
            var blobName = Path.GetFileName(new Uri(blobUrl).LocalPath);
            var blobClient = new BlobClient(conn!, containerName, blobName);
            await blobClient.DeleteIfExistsAsync();
            sw.Stop();

            _logger.LogInformation("Deleted blob {BlobName}", blobName);

            await _auditLog.LogExternalCallAsync(
                correlationId  : correlationId,
                callSite       : "BlobDelete",
                eventType      : "BlobStorage",
                tenantId       : tenantId,
                intakeRecordId : null,
                requestUrl     : blobUrl,
                httpStatusCode : 200,
                durationMs     : sw.ElapsedMilliseconds,
                isMocked       : false,
                outcome        : "Success");
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Could not delete blob at {BlobUrl}", blobUrl);

            await _auditLog.LogExternalCallAsync(
                correlationId  : correlationId,
                callSite       : "BlobDelete",
                eventType      : "BlobStorage",
                tenantId       : tenantId,
                intakeRecordId : null,
                requestUrl     : blobUrl,
                httpStatusCode : null,
                durationMs     : sw.ElapsedMilliseconds,
                isMocked       : false,
                outcome        : "Error",
                errorMessage   : ex.Message);
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

    public Task<string> UploadToFolderAsync(Stream content, string folderPath, string fileName, string contentType)
    {
        // Combine folder path and file name: {folderPath}/{fileName}
        var blobName = $"{folderPath.TrimEnd('/')}/{fileName}";
        return UploadAsync(content, blobName, contentType);
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
