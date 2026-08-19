namespace Bloxstrap.Models.BloxstrapRPC;

public class WallpaperMessage
{
    [JsonPropertyName("assetId")]
    public ulong? AssetId { get; set; }

    [JsonPropertyName("style")]
    public string? Style { get; set; }

    [JsonPropertyName("reset")]
    public bool? Reset { get; set; }
}