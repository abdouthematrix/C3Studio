using System.IO;
using System.Text;
using Microsoft.Xna.Framework;

namespace C3Studio.Infrastructure.C3Format;

/// <summary>Point-light definition stored in a OMNI chunk.</summary>
public class C3Omni
{
    public string  Name        { get; set; } = string.Empty;
    public Vector3 Position    { get; set; }
    public Vector3 Color       { get; set; }
    public float   Radius      { get; set; } = 10f;
    public float   Attenuation { get; set; } = 1f;

    public static C3Omni Load(BinaryReader br)
    {
        var o = new C3Omni();
        uint nl = br.ReadUInt32();
        o.Name = Encoding.ASCII.GetString(br.ReadBytes((int)nl)).TrimEnd('\0');
        o.Position = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        o.Color = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        return o;
    }
}
