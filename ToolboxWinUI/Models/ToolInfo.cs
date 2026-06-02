using System.Text.Json.Serialization;

namespace ToolboxWinUI.Models;

public class ToolInfo
{
    [JsonPropertyName("Icon")]
    public string Icon { get; set; } = "";

    [JsonPropertyName("Name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("Description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("Action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("Category")]
    public string Category { get; set; } = "";
}
