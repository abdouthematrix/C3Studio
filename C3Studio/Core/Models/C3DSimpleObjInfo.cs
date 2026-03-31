namespace C3Studio.Core.Models;

public class C3DSimpleObjInfo
{
    public const int MaxParts = 4;
    public uint   IdType    { get; set; }
    public int    Parts     { get; set; }
    public uint[] MeshIds   { get; } = new uint[MaxParts];
    public uint[] TextureIds{ get; } = new uint[MaxParts];
}
