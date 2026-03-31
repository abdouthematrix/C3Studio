namespace C3Studio.Core.Models;

/// <summary>
/// One entry from <c>ini/3DEffect.ini</c>.
/// Mirrors <c>CMy3DEffectInfo</c> from the C++ client.
/// </summary>
/// <remarks>
/// Section headers may be bare integers (<c>[10000]</c>) or alphanumeric tokens
/// (<c>[1ghost]</c>). <see cref="Key"/> always holds the raw title string;
/// <see cref="Id"/> is only valid when the title is a pure integer.
/// </remarks>
public sealed class C3DEffectInfo
{
    public const int MaxEffects = 16;

    /// <summary>
    /// Raw section title — always set. Mirrors C++ <c>szIndex</c>.
    /// Examples: <c>"10000"</c>, <c>"1ghost"</c>.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Numeric form of <see cref="Key"/> when the title is a pure integer;
    /// 0 otherwise. Convenient for ID-based lookups.
    /// </summary>
    public uint Id { get; set; }

    /// <summary>How many effect slots are active (drives the per-slot arrays).</summary>
    public int Amount { get; set; }

    // ── Per-slot arrays (index 0 … Amount-1) ──────────────────────────────
    public uint[] EffectIds { get; } = new uint[MaxEffects];
    public uint[] TextureIds { get; } = new uint[MaxEffects];

    /// <summary>Alpha source blend; default 5 when absent.</summary>
    public int[] Asb { get; } = new int[MaxEffects];

    /// <summary>Alpha destination blend; default 6 when absent.</summary>
    public int[] Adb { get; } = new int[MaxEffects];

    // ── Scalar fields ──────────────────────────────────────────────────────
    public int Delay { get; set; }
    public int LoopTime { get; set; }
    public int FrameInterval { get; set; }
    public int LoopInterval { get; set; }
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }

    /// <summary>Default 0 when absent (optional in file).</summary>
    public int OffsetZ { get; set; }

    /// <summary>Default 0 when absent (optional in file).</summary>
    public int ShapeAir { get; set; }

    /// <summary>ColorEnable flag; present in the INI but not in original C++ struct — stored for completeness.</summary>
    public int ColorEnable { get; set; }

    /// <summary>Effect level / tier; present on some entries (e.g. <c>Lev=4</c>).</summary>
    public int Lev { get; set; }

    public C3DEffectInfo()
    {
        // Match C++ defaults for optional blend fields
        for (int i = 0; i < MaxEffects; i++)
        {
            Asb[i] = 5;
            Adb[i] = 6;
        }
    }
}