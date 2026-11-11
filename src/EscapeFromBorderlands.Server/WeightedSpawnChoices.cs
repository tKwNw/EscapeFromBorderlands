using SPTarkov.Server.Core.Models.Common;

namespace EscapeFromBorderlands.Server;

// Preserve the original order, floating-point subtraction and final-choice
// fallback. Building a table is independent of drawing a random number.
internal sealed class WeightedSpawnChoices
{
    private readonly VariantDefinition[] _variants;
    private readonly MongoId[] _ids;
    private readonly double _weight;
    internal static readonly WeightedSpawnChoices Empty = new([]);
    internal bool IsEmpty => _variants.Length == 0;

    internal WeightedSpawnChoices(VariantDefinition[] variants)
    {
        _variants = variants;
        _ids = variants.Select(v => new MongoId(v.TemplateId)).ToArray();
        _weight = variants.Sum(v => v.SpawnWeight);
    }

    internal (VariantDefinition Variant, MongoId Id) Pick(double sample)
    {
        var weight = sample * _weight;
        for (var i = 0; i < _variants.Length; i++)
        {
            weight -= _variants[i].SpawnWeight;
            if (weight < 0d) return (_variants[i], _ids[i]);
        }
        return (_variants[^1], _ids[^1]);
    }
}
