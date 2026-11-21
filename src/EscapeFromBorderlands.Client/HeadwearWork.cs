using System;

namespace EscapeFromBorderlands.Client;

[Flags]
internal enum HeadwearInputs
{
    None = 0, Position = 1, Still = 2, Sprint = 4, Posture = 8, Melee = 16,
    Forward = 32, Lean = 64, Aim = 128
}

// Capabilities, not current multiplier values: conditional effects must remain
// eligible before they activate. Catalog/signature coverage is audited offline.
internal static class HeadwearWork
{
    internal static bool Firearm(string key) => key is
        "HW001-2" or "HW003-2" or "HW004-2" or "HW006-2" or "HW008-1" or
        "HW013-1" or "HW015-1" or "HW017-1" or "HW018-1" or "HW019-2" or
        "HW021-1" or "HW027-1" or "HW031-2" or "HW032-1" or "HW037-1" or
        "HW039-1" or "HW044-1" or "HW045-1" or "HW047-3" or "HW049-2" or
        "HW050-3" or "HW051-3" or "HW058-3" or "HW061-1" or "HW062-3" or
        "HW063-1" or "HW064-2" or "HW069-3" or "HW070-2" or "HW074-3" or
        "HW075-2" or "HW077-2" or "HW080-1" or "HW082-3" or "HW091-3";

    internal static bool Movement(string key) => key is
        "HW002-1" or "HW003-2" or "HW004-2" or "HW006-2" or "HW016-1" or
        "HW017-1" or "HW018-1" or "HW019-1" or "HW020-1" or "HW025-1" or
        "HW037-1" or "HW044-1" or "HW047-3" or "HW052-1" or "HW064-2" or
        "HW074-3" or "HW076-3" or "HW078-1" or "HW079-1" or "HW088-1" or "HW093-2";

    internal static HeadwearInputs Inputs(string key) => key switch
    {
        "HW005-1" => HeadwearInputs.Position | HeadwearInputs.Still | HeadwearInputs.Sprint,
        "HW090-1" => HeadwearInputs.Position | HeadwearInputs.Sprint,
        "HW012-1" or "HW016-1" or "HW031-2" or "HW061-1" or "HW092-2" => HeadwearInputs.Still,
        "HW026-1" => HeadwearInputs.Still | HeadwearInputs.Posture,
        "HW014-1" or "HW076-3" => HeadwearInputs.Posture,
        "HW025-1" or "HW066-3" or "HW067-1" or "HW083-2" => HeadwearInputs.Melee,
        "HW070-2" => HeadwearInputs.Sprint,
        "HW048-2" => HeadwearInputs.Forward,
        "HW015-1" => HeadwearInputs.Lean,
        "HW049-2" => HeadwearInputs.Aim,
        _ => HeadwearInputs.None
    };
}
