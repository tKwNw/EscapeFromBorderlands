using SPTarkov.Server.Core.Models.Spt.Mod;
using SemVersion = SemanticVersioning.Version;
using SemRange = SemanticVersioning.Range;

namespace EscapeFromBorderlands.Server;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.tKwNw.escapefromborderlands";
    public override string Name { get; init; } = "EscapeFromBorderlands";
    public override string Author { get; init; } = "tKwNw";
    public override List<string>? Contributors { get; init; } = ["OpenAI Codex"];
    public override SemVersion Version { get; init; } = new("0.31.5", false);
    public override SemRange SptVersion { get; init; } = new("~4.0.13", false);
    public override List<string>? Incompatibilities { get; init; } = [];
    public override Dictionary<string, SemRange>? ModDependencies { get; init; } = [];
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
}
