using System;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

// TMP can retain a live font object after its material has been unloaded.
// Validate both the font's base material and the selected shared material
// before cloning or measuring labels, and let OnEnable run before measuring.
internal static class EffectStatusText
{
    internal readonly struct Assets
    {
        internal readonly UnityEngine.Object Font, BaseMaterial, SharedMaterial;
        internal Assets(UnityEngine.Object font, UnityEngine.Object baseMaterial, UnityEngine.Object sharedMaterial)
        { Font = font; BaseMaterial = baseMaterial; SharedMaterial = sharedMaterial; }
        internal bool Ready => Font != null && BaseMaterial != null && SharedMaterial != null;
    }

    internal static bool TryAssets(Component? source, out Assets assets)
    {
        assets = default;
        if (source == null || Native.Get(source, "font") is not UnityEngine.Object font || font == null) return false;
        if (Native.Get(font, "material") is not UnityEngine.Object material || material == null) return false;
        var shared = Native.Get(source, "fontSharedMaterial") as UnityEngine.Object;
        assets = new Assets(font, material, shared != null ? shared : material);
        return true;
    }

    internal sealed class Layout
    {
        private readonly Component Label;
        private int MeasureAfterFrame;
        private string? Text;
        private bool Measured, Bound;
        private Assets BoundAssets;
        private int ProbeAfterFrame = -1;

        internal void Revalidate() => Bound = false;
        internal float Height { get; private set; } = 25f;

        internal Layout(Component label, int frame) { Label = label; MeasureAfterFrame = frame; }
        internal bool Update(Assets assets, string text, int frame)
        {
            if (Label == null || !assets.Ready) return false;
            // These labels are owned by the HUD. Recheck native bindings after
            // enable or asset changes, and once after TMP has initialized them.
            var changedAssets = !Bound ||
                !ReferenceEquals(BoundAssets.Font, assets.Font) ||
                !ReferenceEquals(BoundAssets.BaseMaterial, assets.BaseMaterial) ||
                !ReferenceEquals(BoundAssets.SharedMaterial, assets.SharedMaterial);
            if (changedAssets || ProbeAfterFrame >= 0 && frame > ProbeAfterFrame)
            {
                var rebound = false;
                if (!ReferenceEquals(Native.Get(Label, "font"), assets.Font))
                { Native.Set(Label, "font", assets.Font); rebound = true; }
                if (!ReferenceEquals(Native.Get(Label, "fontSharedMaterial"), assets.SharedMaterial))
                { Native.Set(Label, "fontSharedMaterial", assets.SharedMaterial); rebound = true; }
                if (rebound) { Measured = false; MeasureAfterFrame = frame; }
                ProbeAfterFrame = changedAssets || rebound ? frame : -1;
                Bound = true; BoundAssets = assets;
            }
            if (Text != text) { Native.Set(Label, "text", text); Text = text; Measured = false; }
            if (!Measured && frame > MeasureAfterFrame)
            {
                var preferred = Native.Number(Label, "preferredHeight");
                if (float.IsNaN(preferred) || float.IsInfinity(preferred)) preferred = 25f;
                Height = Math.Max(25f, preferred + 4f);
                Measured = true;
            }
            return true;
        }
    }
}
