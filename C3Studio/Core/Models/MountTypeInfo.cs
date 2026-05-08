namespace C3Studio.Core.Models;

/// <summary>
/// One entry from <c>ini/Mount.ini</c>.
/// Supports both the old single-part format (<c>[Mount410301]</c> with bare
/// <c>Mesh=</c> / <c>Texture=</c> / optional <c>Texture2=</c> / <c>MoveRateX/Y=</c>)
/// and the new multi-part format (<c>[350001]</c> with <c>Part=</c>, <c>Mesh0=</c>,
/// <c>Texture0=</c>, <c>Asb0=</c>, <c>Adb0=</c> per-slot keys).
/// </summary>
public sealed class MountTypeInfo
{
    public const int MaxParts = 4;

    public uint Id { get; set; }
    public int SubType => (int)(Id / 10000);
    public int Level => (int)(Id % 100);
    public int Parts { get; set; } = 1;

    // Per-slot arrays (index 0 = the single part in old-format entries)
    public uint[] MeshIds { get; } = new uint[MaxParts];
    public uint[] TextureIds { get; } = new uint[MaxParts];

    // Old-format extras (slot 0 only; new format ignores these)
    public uint[] Texture2Ids { get; } = new uint[MaxParts];
    public float[] MoveRateX { get; } = new float[MaxParts];
    public float[] MoveRateY { get; } = new float[MaxParts];

    // New-format blend state per slot
    public int[] Asb { get; } = new int[MaxParts];
    public int[] Adb { get; } = new int[MaxParts];
}