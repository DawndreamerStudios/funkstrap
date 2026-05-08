using System.Text.Json.Serialization;

namespace Bloxstrap.Models.BloxstrapRPC;

public class DesktopVisibilityMessage
{
    [JsonPropertyName("taskbar")]
    public bool? Taskbar { get; set; }

    [JsonPropertyName("desktopIcons")]
    public bool? DesktopIcons { get; set; }

    [JsonPropertyName("reset")]
    public bool? REset { get; set; }
}