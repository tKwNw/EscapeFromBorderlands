using SPTarkov.Server.Core.Models.Spt.Mod;
using SemVersion = SemanticVersioning.Version;
using SemRange = SemanticVersioning.Range;

namespace EscapeFromBorderlands.Server;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.tKwNw.escapefromborderlands";
    public string Name { get; init; } = "EscapeFromBorderlands";
    public string Author { get; init; } = "tKwNw";
    public List<string>? Contributors { get; init; } = ["OpenAI Codex"];
    public SemVersion Version { get; init; } = new("0.40.5", false);
    public SemRange SptVersion { get; init; } = new("~4.1.6", false);
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; } = [];
    public Dictionary<string, SemRange>? ModDependencies { get; init; } = [];
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
}
