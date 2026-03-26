using System.IO;
using System.Text;

namespace C3Studio.Infrastructure.C3Format;

/// <summary>8-byte chunk header preceding every block in a .c3 file.</summary>
public class ChunkHeader
{
    public byte[] ChunkID   { get; private set; } = new byte[4];
    public uint   ChunkSize { get; private set; }
    public string Tag       => Encoding.ASCII.GetString(ChunkID);

    public static ChunkHeader Read(BinaryReader br)
    {
        var h       = new ChunkHeader();
        h.ChunkID   = br.ReadBytes(4);
        h.ChunkSize = br.ReadUInt32();
        return h;
    }

    public override string ToString() => $"{Tag} (size={ChunkSize})";
}
