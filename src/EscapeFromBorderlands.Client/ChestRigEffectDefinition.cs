using System.Collections.Generic;
namespace EscapeFromBorderlands.Client;
internal sealed class ChestRigEffectDefinition
{
    public string Key { get; set; } = string.Empty;
    public bool HasTurningPenalty { get; set; }
    public float TurningPenalty { get; set; }
    public bool HasStorage { get; set; }
    public bool ExpandEveryGrid { get; set; }
    public int EveryGridRows { get; set; }
    public int EveryGridColumns { get; set; }
    public int SoftArmorClass { get; set; }
    public int ExtraRows { get; set; }
    public int ExtraColumns { get; set; }
    public List<ChestRigGridDefinition> ExtraGrids { get; set; } = new();
}
internal sealed class ChestRigGridDefinition
{
    public int Width { get; set; }
    public int Height { get; set; }
    public List<string> Filter { get; set; } = new();
}
