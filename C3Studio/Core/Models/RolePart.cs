namespace C3Studio.Core.Models;

public enum RolePartType
{
    Armor,
    Armet,
    Weapon,
    Head,
    Cape,
    Misc,
    Mount,
    Pelvis,
    Spirit
}

public class RolePart
{
    public const int MaxParts = 8; // Shared maximum boundary for component slots

    public uint Id { get; set; }
    public RolePartType PartType { get; set; }
    public int Look => (int)(Id / 1_000_000);
    public int SubType
    {
        get
        {
            switch (PartType)
            {
                case RolePartType.Mount:
                    return (int)(Id / 10000);                                
                case RolePartType.Spirit:
                    return (int)(Id / 10000);
                default:
                    return (int)((Id % 1_000_000) / 1_000);
            }            
        }
    }
    public int Level => (int)(Id % 100);
    public int Parts { get; set; }

    public uint[] MeshIds { get; init; } = new uint[MaxParts];
    public uint[] TextureIds { get; init; } = new uint[MaxParts];
    public int[] Asb { get; init; } = new int[MaxParts];
    public int[] Adb { get; init; } = new int[MaxParts];
}