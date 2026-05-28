using System.IO;
using System.IO.Compression;
using System.Text;

namespace C3Studio.Infrastructure.FileSystem;

internal sealed class TpiPackageReader : IPackageReader
{
    private const string TpiMagic = "NetDragonDatPkg";
    private const long TpiVersion = 1000;

    private struct Entry
    {
        public uint Offset;
        public uint CompressedSize;
        public uint UncompressedSize;
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly FileStream _tpdFile;

    public TpiPackageReader(string tpiPath)
    {
        var tpdPath = Path.ChangeExtension(tpiPath, ".tpd");
        if (!File.Exists(tpdPath))
            throw new FileNotFoundException($"TPD file not found for: {tpiPath}");

        _tpdFile = new FileStream(tpdPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var encoding = Encoding.Latin1; // byte-identical to Windows-1252 for path characters; always available on .NET Core/5+

        // Read entire TPI into memory so we can do safe bounds-checked parsing
        // (mirrors the JS approach of slicing the full index buffer before iterating)
        byte[] tpiBytes;
        using (var tpiStream = new FileStream(tpiPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            tpiBytes = new byte[tpiStream.Length];
            tpiStream.ReadExactly(tpiBytes);
        }

        if (tpiBytes.Length < 0x30)
            throw new InvalidDataException($"TPI file too small: {tpiPath}");

        // Validate magic (first 16 bytes, null-padded)
        var magic = Encoding.ASCII.GetString(tpiBytes, 0, TpiMagic.Length);
        if (magic != TpiMagic)
            throw new InvalidDataException($"Invalid TPI header in: {tpiPath}");

        // Parse header fields at fixed offsets (struct is Pack=1, no padding)
        // offset 0x00 : Identifier[16]
        // offset 0x10 : Version (Int64)
        // offset 0x18 : Unknown1 (Int32)
        // offset 0x1C : Unknown2 (Int32)
        // offset 0x20 : Unknown3 (Int32)
        // offset 0x24 : Number   (UInt32)  ← confirmed by JS: hv.getUint32(0x24)
        // offset 0x28 : Offset   (UInt32)
        // offset 0x2C : Reserved (Int32)
        // total header = 0x30 bytes
        var version = BitConverter.ToInt64(tpiBytes, 0x10);
        if (version != TpiVersion)
            throw new InvalidDataException($"Unsupported TPI version {version} in: {tpiPath}");

        var count = BitConverter.ToUInt32(tpiBytes, 0x24);

        // Walk the index section with an explicit bounds-checked cursor
        var pos = 0x30;
        for (var i = 0; i < count; i++)
        {
            // Need at least 1 byte for pathLen
            if (pos >= tpiBytes.Length) break;

            var pathLen = tpiBytes[pos];
            pos += 1;

            // Need pathLen bytes + 2 (unknown) + 4*4 (sizes) + 4 (offset) = pathLen + 18
            if (pos + pathLen + 18 > tpiBytes.Length) break;

            var path = encoding.GetString(tpiBytes, pos, pathLen);
            pos += pathLen;

            pos += 2; // Unknown1 (Int16, always 0x01)

            var uncompressedSize = BitConverter.ToUInt32(tpiBytes, pos); pos += 4;
            var compressedSize = BitConverter.ToUInt32(tpiBytes, pos); pos += 4;
            var compressedSize2 = BitConverter.ToUInt32(tpiBytes, pos); pos += 4;
            var uncompressedSize2 = BitConverter.ToUInt32(tpiBytes, pos); pos += 4;
            var offset = BitConverter.ToUInt32(tpiBytes, pos); pos += 4;

            // Both size-pair fields must agree — skip malformed entries
            if (compressedSize != compressedSize2 || uncompressedSize != uncompressedSize2)
                continue;

            // Normalise to lowercase forward-slash paths, no leading separator
            var normalised = path.ToLowerInvariant().Replace('\\', '/').TrimStart('/');
            _entries.TryAdd(normalised, new Entry
            {
                Offset = offset,
                CompressedSize = compressedSize,
                UncompressedSize = uncompressedSize
            });
        }
    }

    public void AddPackage(string fileName) => throw new NotSupportedException();

    public Stream LoadFile(string fileName)
    {
        // Strip the leading package-key segment that TqPackageReader prepends
        // e.g. "c3/map/1002.dmap" → "map/1002.dmap"
        var normalised = fileName.ToLowerInvariant().Replace('\\', '/').TrimStart('/');
        var slash = normalised.IndexOf('/');
        var relativePath = slash >= 0 ? normalised[(slash + 1)..] : normalised;

        if (!_entries.TryGetValue(relativePath, out var entry))
            if (!_entries.TryGetValue(normalised, out entry))
                throw new FileNotFoundException($"File not found in TPI: {fileName}");

        // Read compressed bytes from TPD, using ReadExactly so we never get a partial buffer
        _tpdFile.Seek(entry.Offset, SeekOrigin.Begin);
        var compBuffer = new byte[entry.CompressedSize];
        _tpdFile.ReadExactly(compBuffer);

        // Stored (uncompressed) — return directly
        if (entry.CompressedSize == entry.UncompressedSize)
            return new MemoryStream(compBuffer, writable: false);

        // Decompress — try ZLib (standard wrapper used by the original CO2_CORE_DLL),
        // fall back to raw Deflate if the stream has no zlib header
        return Decompress(compBuffer, entry.UncompressedSize);
    }

    private static MemoryStream Decompress(byte[] compBuffer, uint expectedSize)
    {
        // Try zlib format first (0x78 header byte = zlib magic)
        if (compBuffer.Length >= 2 && compBuffer[0] == 0x78)
        {
            try
            {
                using var cs = new MemoryStream(compBuffer);
                using var zlib = new ZLibStream(cs, CompressionMode.Decompress);
                var output = new MemoryStream((int)expectedSize);
                zlib.CopyTo(output);
                output.Position = 0;
                return output;
            }
            catch { /* fall through to raw deflate */ }
        }

        // Raw deflate fallback
        using var rs = new MemoryStream(compBuffer);
        using var deflate = new DeflateStream(rs, CompressionMode.Decompress);
        var result = new MemoryStream((int)expectedSize);
        deflate.CopyTo(result);
        result.Position = 0;
        return result;
    }

    public void Dispose()
    {
        _entries.Clear();
        _tpdFile.Dispose();
    }
}