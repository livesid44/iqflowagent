using System.Text;
using System.Text.Json;

namespace IQFlowAgent.Web.Services;

public class AzureEmbeddingService : IAzureEmbeddingService
{
    private const string PlaceholderEndpoint   = "YOUR_RESOURCE";
    private const string PlaceholderKey        = "YOUR_API_KEY";
    private const string PlaceholderDeployment = "YOUR_DEPLOYMENT_NAME";
    private const string DefaultEmbeddingModel = "text-embedding-3-small";

    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITenantContextService _tenantContext;
    private readonly ILogger<AzureEmbeddingService> _logger;

    public AzureEmbeddingService(
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ITenantContextService tenantContext,
        ILogger<AzureEmbeddingService> logger)
    {
        _config            = config;
        _httpClientFactory = httpClientFactory;
        _tenantContext     = tenantContext;
        _logger            = logger;
    }

    public bool IsConfigured()
    {
        var cfg = GetEmbeddingConfigAsync().GetAwaiter().GetResult();
        return cfg.HasValue;
    }

    public async Task<float[]?> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var cfg = await GetEmbeddingConfigAsync();
        if (!cfg.HasValue) return null;
        var (endpoint, apiKey, deployment, apiVersion) = cfg.Value;

        var url = $"{endpoint.TrimEnd('/')}/openai/deployments/{deployment}/embeddings?api-version={apiVersion}";
        try
        {
            var http = _httpClientFactory.CreateClient();
            http.DefaultRequestHeaders.Add("api-key", apiKey);

            var body = JsonSerializer.Serialize(new { input = text });
            var resp = await http.PostAsync(url,
                new StringContent(body, Encoding.UTF8, "application/json"), ct);

            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Azure Embedding API returned {Code}: {Body}", (int)resp.StatusCode, err);
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var dataArr = doc.RootElement.GetProperty("data");
            if (dataArr.GetArrayLength() == 0) return null;

            var embArr = dataArr[0].GetProperty("embedding");
            var floats = new float[embArr.GetArrayLength()];
            int i = 0;
            foreach (var v in embArr.EnumerateArray())
                floats[i++] = v.GetSingle();
            return floats;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetEmbeddingAsync failed — returning null.");
            return null;
        }
    }

    public async Task<List<float[]?>> GetEmbeddingsBatchAsync(
        IEnumerable<string> texts, CancellationToken ct = default)
    {
        var result = new List<float[]?>();
        foreach (var text in texts)
        {
            ct.ThrowIfCancellationRequested();
            result.Add(await GetEmbeddingAsync(text, ct));
        }
        return result;
    }

    // ── Config resolution ───────────────────────────────────────────────────

    private async Task<(string endpoint, string apiKey, string deployment, string apiVersion)?> GetEmbeddingConfigAsync()
    {
        var tenantSettings = await _tenantContext.GetCurrentTenantAiSettingsAsync();

        string? endpoint, apiKey, deployment, apiVersion;

        if (tenantSettings != null
            && !string.IsNullOrWhiteSpace(tenantSettings.AzureOpenAIEndpoint)
            && !string.IsNullOrWhiteSpace(tenantSettings.AzureOpenAIApiKey)
            && !tenantSettings.AzureOpenAIEndpoint.Contains(PlaceholderEndpoint, StringComparison.OrdinalIgnoreCase)
            && !tenantSettings.AzureOpenAIApiKey.Contains(PlaceholderKey,        StringComparison.OrdinalIgnoreCase))
        {
            endpoint   = tenantSettings.AzureOpenAIEndpoint;
            apiKey     = tenantSettings.AzureOpenAIApiKey;
            apiVersion = tenantSettings.AzureOpenAIApiVersion;
            deployment = !string.IsNullOrWhiteSpace(tenantSettings.AzureOpenAIEmbeddingDeployment)
                ? tenantSettings.AzureOpenAIEmbeddingDeployment
                : (_config["AzureOpenAI:EmbeddingDeployment"] ?? DefaultEmbeddingModel);
        }
        else
        {
            endpoint   = _config["AzureOpenAI:Endpoint"];
            apiKey     = _config["AzureOpenAI:ApiKey"];
            apiVersion = _config["AzureOpenAI:ApiVersion"] ?? "2025-01-01-preview";
            deployment = _config["AzureOpenAI:EmbeddingDeployment"] ?? DefaultEmbeddingModel;
        }

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey)
            || endpoint.Contains(PlaceholderEndpoint, StringComparison.OrdinalIgnoreCase)
            || apiKey.Contains(PlaceholderKey,        StringComparison.OrdinalIgnoreCase))
            return null;

        return (endpoint, apiKey, deployment, apiVersion);
    }
}
