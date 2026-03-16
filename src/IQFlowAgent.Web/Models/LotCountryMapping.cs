namespace IQFlowAgent.Web.Models;

/// <summary>
/// Maps a specific LOT (SdcLots value) to allowed countries and cities for a tenant.
/// When UseCountryFilterByLot is enabled on TenantAiSettings, only the countries/cities
/// configured here will appear in the intake form when that LOT is selected.
/// </summary>
public class LotCountryMapping
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;

    /// <summary>LOT name exactly as stored in SdcLots (e.g. "Lot 1 – Global Customer Support").</summary>
    public string LotName { get; set; } = string.Empty;

    /// <summary>Country name (matches values used in country-city-data.js).</summary>
    public string Country { get; set; } = string.Empty;

    /// <summary>Comma-separated city list. Empty means "all cities for this country".</summary>
    public string Cities { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
