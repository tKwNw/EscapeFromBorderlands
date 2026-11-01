namespace EscapeFromBorderlands.Server;
internal sealed class BackpackEffectDefinition
{
    public string Key { get; set; } = string.Empty;
    public int ExtraRows { get; set; }
    public int ExtraColumns { get; set; }
    public System.Collections.Generic.List<BackpackGridDefinition> ExtraGrids { get; set; } = new();
}
internal sealed class BackpackGridDefinition
{
    public int Width { get; set; }
    public int Height { get; set; }
    public bool FirearmsOnly { get; set; }
}
