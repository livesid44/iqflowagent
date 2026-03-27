using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IQFlowAgent.Web.Services;

public class AzureSearchService : IAzureSearchService
{
    private const string ApiVersion           = "2024-07-01";
    private const string DefaultIndexName     = "iqflow-rag-chunks";
    private const int    VectorDims           = 1536;
    private const int    UploadBatchSize      = 100;

    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITenantContextService _tenantContext;
    private readonly ILogger<AzureSearchService> _logger;

    public AzureSearchService(
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ITenantContextService tenantContext,
        ILogger<AzureSearchService> logger)
    {
        _config            = config;
        _httpClientFactory = httpClientFactory;
        _tenantContext     = tenantContext;
        _logger            = logger;
    }

    public bool IsConfigured()
    {
        var cfg = GetSearchConfigAsync().GetAwaiter().GetResult();
        return cfg.HasValue;
    }

    public async Task EnsureIndexExistsAsync(CancellationToken ct = default)
    {
        var cfg = await GetSearchConfigAsync();
        if (!cfg.HasValue) return;
        var (endpoint, apiKey, indexName) = cfg.Value;

        var url = $"{endpoint.TrimEnd('/')}/indexes/{indexName}?api-version={ApiVersion}";
        var indexDef = new
        {
            name = indexName,
            fields = new object[]
            {
                new { name = "id",              type = "Edm.String",              key = true,  searchable = false, filterable = false },
                new { name = "intakeDbId",      type = "Edm.Int32",               key = false, searchable = false, filterable = true  },
                new { name = "intakePublicId",  type = "Edm.String",              key = false, searchable = false, filterable = true  },
                new { name = "tenantId",        type = "Edm.Int32",               key = false, searchable = false, filterable = true  },
                new { name = "folderPath",      type = "Edm.String",              key = false, searchable = false, filterable = true  },
                new { name = "documentName",    type = "Edm.String",              key = false, searchable = true,  filterable = false },
                new { name = "content",         type = "Edm.String",              key = false, searchable = true,  filterable = false },
                new { name = "chunkIndex",      type = "Edm.Int32",               key = false, searchable = false, filterable = false },
                new
                {
                    name = "contentVector",
                    type = "Collection(Edm.Single)",
                    key = false,
                    searchable = true,
                    filterable = false,
                    dimensions = VectorDims,
                    vectorSearchProfile = "default-profile"
                }
            },
            vectorSearch = new
            {
                algorithms = new object[]
                {
                    new { name = "default-hnsw", kind = "hnsw", hnswParameters = new { metric = "cosine" } }
                },
                profiles = new object[]
                {
                    new { name = "default-profile", algorithm = "default-hnsw" }
                }
            }
        };

        try
        {
            var http = CreateClient(apiKey);
            var body = JsonSerializer.Serialize(indexDef);
            var resp = await http.PutAsync(url,
                new StringContent(body, Encoding.UTF8, "application/json"), ct);

            if (!resp.IsSuccessStatusCode && (int)resp.StatusCode != 409)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("EnsureIndexExistsAsync: PUT returned {Code}: {Body}", (int)resp.StatusCode, err[..Math.Min(500, err.Length)]);
            }
            else
            {
                _logger.LogInformation("Azure AI Search index '{Index}' is ready.", indexName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EnsureIndexExistsAsync failed — search index may not be available.");
        }
    }

    public async Task IndexChunksAsync(IEnumerable<DocumentChunkRecord> chunks, CancellationToken ct = default)
    {
        var cfg = await GetSearchConfigAsync();
        if (!cfg.HasValue) return;
        var (endpoint, apiKey, indexName) = cfg.Value;

        var url = $"{endpoint.TrimEnd('/')}/indexes/{indexName}/docs/index?api-version={ApiVersion}";
        var http = CreateClient(apiKey);

        var batch = chunks.ToList();
        for (int i = 0; i < batch.Count; i += UploadBatchSize)
        {
            var slice = batch.Skip(i).Take(UploadBatchSize).ToList();

            var actions = slice.Select(c =>
            {
                var obj = new JsonObject
                {
                    ["@search.action"] = "upload",
                    ["id"]             = c.Id,
                    ["intakeDbId"]     = c.IntakeDbId,
                    ["intakePublicId"] = c.IntakePublicId,
                    ["tenantId"]       = c.TenantId,
                    ["folderPath"]     = c.FolderPath,
                    ["documentName"]   = c.DocumentName,
                    ["content"]        = c.Content,
                    ["chunkIndex"]     = c.ChunkIndex,
                };
                var vecArr = new JsonArray();
                foreach (var f in c.Embedding) vecArr.Add(f);
                obj["contentVector"] = vecArr;
                return obj;
            });

            var payload = new JsonObject { ["value"] = new JsonArray(actions.ToArray<JsonNode?>()) };
            var body = payload.ToJsonString();

            try
            {
                var resp = await http.PostAsync(url,
                    new StringContent(body, Encoding.UTF8, "application/json"), ct);
                if (!resp.IsSuccessStatusCode)
                {
                    var err = await resp.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning("IndexChunksAsync batch {I}: HTTP {Code}: {Body}", i / UploadBatchSize, (int)resp.StatusCode, err[..Math.Min(500, err.Length)]);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IndexChunksAsync batch {I} failed.", i / UploadBatchSize);
            }
        }
    }

    public async Task DeleteIntakeChunksAsync(int intakeDbId, CancellationToken ct = default)
    {
        var cfg = await GetSearchConfigAsync();
        if (!cfg.HasValue) return;
        var (endpoint, apiKey, indexName) = cfg.Value;
        var http = CreateClient(apiKey);

        var searchUrl = $"{endpoint.TrimEnd('/')}/indexes/{indexName}/docs/search?api-version={ApiVersion}";
        var searchBody = JsonSerializer.Serialize(new
        {
            filter  = $"intakeDbId eq {intakeDbId}",
            select  = "id",
            top     = 1000
        });

        try
        {
            var searchResp = await http.PostAsync(searchUrl,
                new StringContent(searchBody, Encoding.UTF8, "application/json"), ct);
            if (!searchResp.IsSuccessStatusCode) return;

            var searchJson = await searchResp.Content.ReadAsStringAsync(ct);
            using var searchDoc = JsonDocument.Parse(searchJson);
            if (!searchDoc.RootElement.TryGetProperty("value", out var valArr)) return;

            var ids = valArr.EnumerateArray()
                .Where(e => e.TryGetProperty("id", out _))
                .Select(e => e.GetProperty("id").GetString()!)
                .ToList();

            if (ids.Count == 0) return;

            var deleteUrl = $"{endpoint.TrimEnd('/')}/indexes/{indexName}/docs/index?api-version={ApiVersion}";
            var deleteActions = ids.Select(id => new JsonObject
            {
                ["@search.action"] = "delete",
                ["id"]             = id
            });
            var deletePayload = new JsonObject { ["value"] = new JsonArray(deleteActions.ToArray<JsonNode?>()) };

            await http.PostAsync(deleteUrl,
                new StringContent(deletePayload.ToJsonString(), Encoding.UTF8, "application/json"), ct);

            _logger.LogInformation("Deleted {Count} chunks for intakeDbId={IntakeDbId} from Azure AI Search.", ids.Count, intakeDbId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DeleteIntakeChunksAsync failed for intakeDbId={IntakeDbId}.", intakeDbId);
        }
    }

    public async Task<List<string>> SearchAsync(
        int intakeDbId, float[]? queryVector, string queryText,
        int topK = 5, CancellationToken ct = default)
    {
        var cfg = await GetSearchConfigAsync();
        if (!cfg.HasValue) return [];
        var (endpoint, apiKey, indexName) = cfg.Value;

        var url = $"{endpoint.TrimEnd('/')}/indexes/{indexName}/docs/search?api-version={ApiVersion}";
        var http = CreateClient(apiKey);

        try
        {
            JsonObject requestBody;
            if (queryVector != null && queryVector.Length > 0)
            {
                var vecArr = new JsonArray();
                foreach (var f in queryVector) vecArr.Add(f);

                requestBody = new JsonObject
                {
                    ["search"] = queryText,
                    ["vectorQueries"] = new JsonArray(new JsonObject
                    {
                        ["kind"]   = "vector",
                        ["vector"] = vecArr,
                        ["fields"] = "contentVector",
                        ["k"]      = 10
                    }),
                    ["filter"] = $"intakeDbId eq {intakeDbId}",
                    ["select"] = "content,documentName,chunkIndex",
                    ["top"]    = topK
                };
            }
            else
            {
                requestBody = new JsonObject
                {
                    ["search"] = queryText,
                    ["filter"] = $"intakeDbId eq {intakeDbId}",
                    ["select"] = "content,documentName,chunkIndex",
                    ["top"]    = topK
                };
            }

            var resp = await http.PostAsync(url,
                new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json"), ct);

            if (!resp.IsSuccessStatusCode) return [];

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("value", out var arr)) return [];

            return arr.EnumerateArray()
                .Where(e => e.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                .Select(e => e.GetProperty("content").GetString()!)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SearchAsync failed for intakeDbId={IntakeDbId}, query='{Query}'.", intakeDbId, queryText);
            return [];
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private HttpClient CreateClient(string apiKey)
    {
        var http = _httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Add("api-key", apiKey);
        return http;
    }

    private async Task<(string endpoint, string apiKey, string indexName)?> GetSearchConfigAsync()
    {
        var tenantSettings = await _tenantContext.GetCurrentTenantAiSettingsAsync();

        string? endpoint, apiKey, indexName;

        if (tenantSettings != null
            && !string.IsNullOrWhiteSpace(tenantSettings.AzureSearchEndpoint)
            && !string.IsNullOrWhiteSpace(tenantSettings.AzureSearchApiKey)
            && !tenantSettings.AzureSearchEndpoint.Contains("YOUR_SEARCH",     StringComparison.OrdinalIgnoreCase)
            && !tenantSettings.AzureSearchApiKey.Contains("YOUR_SEARCH_API_KEY", StringComparison.OrdinalIgnoreCase))
        {
            endpoint  = tenantSettings.AzureSearchEndpoint;
            apiKey    = tenantSettings.AzureSearchApiKey;
            indexName = !string.IsNullOrWhiteSpace(tenantSettings.AzureSearchIndexName)
                ? tenantSettings.AzureSearchIndexName
                : DefaultIndexName;
        }
        else
        {
            endpoint  = _config["AzureSearch:Endpoint"];
            apiKey    = _config["AzureSearch:ApiKey"];
            indexName = _config["AzureSearch:IndexName"] ?? DefaultIndexName;
        }

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey)
            || endpoint.Contains("YOUR_SEARCH",       StringComparison.OrdinalIgnoreCase)
            || apiKey.Contains("YOUR_SEARCH_API_KEY", StringComparison.OrdinalIgnoreCase)
            || endpoint.Contains("YOUR_",             StringComparison.OrdinalIgnoreCase)
            || apiKey.Contains("YOUR_",               StringComparison.OrdinalIgnoreCase))
            return null;

        return (endpoint, apiKey, indexName ?? DefaultIndexName);
    }
}
