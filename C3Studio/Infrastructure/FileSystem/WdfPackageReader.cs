using System.IO;
using System.Security.Policy;
using System.Text;

namespace C3Studio.Infrastructure.FileSystem;

/// <summary>
/// Represents one entry in a WDF index table.
/// </summary>
public readonly record struct WdfEntry(uint FileId, uint Offset, uint Size);

/// <summary>
/// Reads asset files out of a Conquer Online <c>.wdf</c> binary archive.
/// <para>
/// WDF format layout:
/// <list type="number">
///   <item>[uint32] magic / version (skipped)</item>
///   <item>[uint32] file count</item>
///   <item>[uint32] absolute offset of the index table</item>
///   <item>… raw file data blocks …</item>
///   <item>Index table: N × { FileId, FileOffset, FileSize, Reserved } (all uint32)</item>
/// </list>
/// Files are addressed by a custom 32-bit hash of their lower-case path string.
/// </para>
/// </summary>
internal sealed class WdfPackageReader : IPackageReader
{
    private struct PackedFile
    {
        public uint FileId;
        public uint FileOffset;
        public uint FileSize;
        public uint Reserved;
    }

    private readonly Dictionary<uint, PackedFile> _packedFiles = new();
    private readonly FileStream _packFile;

    public WdfPackageReader(string fileName)
    {
        _packFile = new FileStream(fileName, FileMode.Open);
        using var reader = new BinaryReader(_packFile, Encoding.ASCII, leaveOpen: true);

        reader.ReadUInt32();
        var fileCount = reader.ReadUInt32();
        var indexOffset = reader.ReadUInt32();

        _packFile.Seek(indexOffset, SeekOrigin.Begin);

        for (var i = 0; i < fileCount; i++)
        {
            var file = new PackedFile
            {
                FileId = reader.ReadUInt32(),
                FileOffset = reader.ReadUInt32(),
                FileSize = reader.ReadUInt32(),
                Reserved = reader.ReadUInt32()                
            };
            _packedFiles.Add(file.FileId, file);
        }
    }

    // ── Public enumeration ───────────────────────────────────────────────

    /// <summary>
    /// All entries present in this archive's index table.
    /// </summary>
    public IEnumerable<WdfEntry> Entries =>
        _packedFiles.Values.Select(f => new WdfEntry(f.FileId, f.FileOffset, f.FileSize));

    /// <summary>
    /// Reads up to <paramref name="maxBytes"/> bytes from the start of the entry
    /// identified by <paramref name="fileId"/>. Returns <c>null</c> when the id
    /// is not present in this archive.
    /// </summary>
    public byte[]? ReadHeader(uint fileId, int maxBytes = 20)
    {
        if (!_packedFiles.TryGetValue(fileId, out var file))
            return null;

        int count = (int)Math.Min((uint)maxBytes, file.FileSize);
        var buffer = new byte[count];
        lock (_packFile)
        {
            _packFile.Seek(file.FileOffset, SeekOrigin.Begin);
            _ = _packFile.Read(buffer, 0, count);
        }
        return buffer;
    }

    // ── IPackageReader ───────────────────────────────────────────────────

    public void AddPackage(string fileName) => throw new NotSupportedException();

    public Stream LoadFile(string fileName)
    {
        uint hash;
        // If caller passed a numeric string, treat it as hash
        // Original filename format: $"{archiveKey}\\{entry.FileId:x8}"
        var separatorIdx = fileName.IndexOf('\\'); // find the backslash
        var hashKey = separatorIdx >= 0
            ? fileName[(separatorIdx + 1)..] // take everything after the backslash
            : fileName;                      // if no backslash, just take the whole string

        if (uint.TryParse(hashKey, System.Globalization.NumberStyles.HexNumber, null, out uint fileId))
            hash = fileId;        
        else        
            hash = HashFilename(fileName); 
        if (!_packedFiles.TryGetValue(hash, out var file))
            throw new FileNotFoundException($"File not found in WDF: {fileName}");

        _packFile.Seek(file.FileOffset, SeekOrigin.Begin);
        var buffer = new byte[file.FileSize];
        _packFile.Read(buffer, 0, (int)file.FileSize);
        return new MemoryStream(buffer, writable: false);
    }

    public void Dispose()
    {
        _packedFiles.Clear();
        _packFile.Dispose();
    }

    // ── Hash ─────────────────────────────────────────────────────────────

    private static uint HashFilename(string filename)
    {
        uint num = 4110059816u;
        uint num2 = 0u;
        uint num3 = 0u;
        uint num4 = 933775118u;
        uint num5 = 2002301995u;
        uint num6 = 0u;
        int num7 = 0;
        uint[] array = new uint[70];
        byte[] bytes = Encoding.ASCII.GetBytes(filename.ToLower());
        byte[] array2 = new byte[bytes.Length + ((bytes.Length % 4 != 0) ? (4 - bytes.Length % 4) : 0)];
        bytes.CopyTo(array2, 0);
        int i;
        using (BinaryReader binaryReader = new BinaryReader(new MemoryStream(array2, writable: false)))
        {
            for (i = 0; i < array2.Length / 4; i++)
            {
                array[i] = (uint)binaryReader.ReadInt32();
            }
        }
        array[i++] = 2615624776u;
        array[i++] = 1727278152u;
        for (num7 = 0; num7 < i; num7++)
        {
            num6 = 645597969u;
            num = (num << 1) | (num >> 31);
            num6 ^= num;
            num2 = array[num7];
            num4 ^= num2;
            num5 ^= num2;
            num3 = num6;
            num3 += num5;
            num3 |= 0x2040801u;
            num3 &= 0xBFEF7FDFu;
            ulong num8 = num3;
            num8 *= num4;
            num2 = (uint)num8;
            num3 = (uint)(num8 >> 32);
            if (num3 != 0)
            {
                num2++;
            }
            num8 = num2;
            num8 += num3;
            num2 = (uint)num8;
            if ((uint)(num8 >> 32) != 0)
            {
                num2++;
            }
            num3 = num6;
            num3 += num4;
            num3 |= 0x804021u;
            num3 &= 0x7DFEFBFFu;
            num4 = num2;
            num8 = num5;
            num8 *= num3;
            num2 = (uint)num8;
            num3 = (uint)(num8 >> 32);
            num8 = num3;
            num8 += num3;
            num3 = (uint)num8;
            if ((uint)(num8 >> 32) != 0)
            {
                num2++;
            }
            num8 = num2;
            num8 += num3;
            num2 = (uint)num8;
            if ((uint)(num8 >> 32) != 0)
            {
                num2 += 2;
            }
            num5 = num2;
        }
        return num4 ^ num5;
    }
}