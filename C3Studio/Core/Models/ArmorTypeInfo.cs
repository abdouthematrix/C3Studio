namespace C3Studio.Core.Models;

/// <summary>
/// Data parsed from a single section of <c>ini/Armor.ini</c>.
/// Section format (numeric header, multi-part):
/// <code>
/// [1000000]
/// Part=1
/// Mesh0=1000000
/// Texture0=1000000
/// Asb0=5
/// Adb0=6
/// </code>
/// </summary>
public class ArmorTypeInfo
{
    public const int MaxParts = 8;

    /// <summary>The raw numeric section ID (e.g. 1000000).</summary>
    public uint Id { get; set; }
    public int Look => (int)(Id / 1_000_000);
    public int SubType
    {
        get
        {
            // Keep only last 6 digits
            int trimmed = (int)(Id % 1_000_000);

            // Extract the first 3 digits of those 6
            int type = trimmed / 1_000;

            return type;
        }
    }

    /// <summary>Number of active mesh/texture slots.</summary>
    public int Parts { get; set; }

    public uint[] MeshIds { get; } = new uint[MaxParts];
    public uint[] TextureIds { get; } = new uint[MaxParts];
    public int[] Asb { get; } = new int[MaxParts];
    public int[] Adb { get; } = new int[MaxParts];
}
