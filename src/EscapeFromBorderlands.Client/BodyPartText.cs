using System;
using System.Collections.Generic;
using EFT;

namespace EscapeFromBorderlands.Client;

internal static class BodyPartText
{
    private static readonly Dictionary<EBodyPart, string> Names = Build();
    private static Dictionary<EBodyPart, string> Build()
    {
        var names = new Dictionary<EBodyPart, string>();
        foreach (EBodyPart part in Enum.GetValues(typeof(EBodyPart)))
            names[part] = part.ToString();
        return names;
    }
    // Preserve the native enum spelling (including aliases/unknown values),
    // without boxing/formatting the common hit regions repeatedly.
    internal static string Of(EBodyPart part) => Names.TryGetValue(part, out var name) ? name : part.ToString();
}
