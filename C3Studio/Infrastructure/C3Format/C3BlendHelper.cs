using Microsoft.Xna.Framework.Graphics;

namespace C3Studio.Infrastructure.C3Format;

/// <summary>
/// Shared D3D9 → MonoGame blend-state resolution used by all C3 chunk renderers.
/// Caches BlendState GPU objects by (D3D src, D3D dst) pair so they are never
/// re-created per frame.
///
/// Common D3DBLEND pairs:
///   (5, 6) SrcAlpha / InvSrcAlpha → standard alpha blend
///   (2, 2) One      / One         → full additive
///   (5, 2) SrcAlpha / One         → alpha-weighted additive (soft glow)
/// </summary>
public static class C3BlendHelper
{
    private static readonly Dictionary<(int, int), BlendState> _cache = new();

    /// <summary>
    /// Returns a cached BlendState for the given D3DBLEND_* integer pair.
    /// </summary>
    public static BlendState Resolve(int asb, int adb)
    {
        //var key = (asb, adb);
        //if (_cache.TryGetValue(key, out var cached)) return cached;

        var src = D3dBlend(asb);
        var dst = D3dBlend(adb);
        var bs = new BlendState
        {
            ColorSourceBlend = src,
            ColorDestinationBlend = dst,
            AlphaSourceBlend = src,
            AlphaDestinationBlend = dst,
        };
        return bs;// _cache[key] = bs;
    }

    /// <summary>
    /// Converts a D3DBLEND integer constant to its MonoGame <see cref="Blend"/> equivalent.
    /// Unknown values fall back to <see cref="Blend.One"/> (safe transparent pass-through).
    /// </summary>
    public static Blend D3dBlend(int d3d) => d3d switch
    {
        1 => Blend.Zero,
        2 => Blend.One,
        3 => Blend.SourceColor,
        4 => Blend.InverseSourceColor,
        5 => Blend.SourceAlpha,
        6 => Blend.InverseSourceAlpha,
        7 => Blend.DestinationAlpha,
        8 => Blend.InverseDestinationAlpha,
        9 => Blend.DestinationColor,
        10 => Blend.InverseDestinationColor,
        11 => Blend.SourceAlphaSaturation,
        _ => Blend.One,
    };
}