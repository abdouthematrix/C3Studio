using System.IO;
using System.Text;

namespace C3Studio.Infrastructure.FileSystem;

internal sealed class DnpPackageReader : IPackageReader
{
    private const string DnpMagic = "DawnPack.TqDigital";
    private const int MaxIdentifierSize = 0x20;
    private const int MinVersion = 1000;
    private const int MaxVersion = 1001;

    // Version 1001 XOR masks
    private const uint XorUid    = 0x95279527;
    private const uint XorSize   = 0x96120059;
    private const uint XorOffset = 0x99589958;

    private struct PackedFile
    {
        public uint Size;
        public uint Offset;
    }

    private readonly Dictionary<uint, PackedFile> _packedFiles = new();
    private readonly FileStream _packFile;

    public DnpPackageReader(string fileName)
    {
        _packFile = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(_packFile, Encoding.ASCII, leaveOpen: true);

        // Validate magic (0x20 bytes, null-padded)
        var magic = Encoding.ASCII.GetString(reader.ReadBytes(MaxIdentifierSize)).TrimEnd('\0');
        if (magic != DnpMagic)
            throw new InvalidDataException($"Invalid DNP header in: {fileName}");

        var version = reader.ReadInt32();
        if (version < MinVersion || version > MaxVersion)
            throw new InvalidDataException($"Unsupported DNP version {version} in: {fileName}");

        var count = reader.ReadInt32();
        var encrypted = version == 1001;

        for (var i = 0; i < count; i++)
        {
            var uid    = reader.ReadUInt32();
            var size   = reader.ReadUInt32();
            var offset = reader.ReadUInt32();

            if (encrypted)
            {
                uid    ^= XorUid;
                size   ^= XorSize;
                offset ^= XorOffset;
            }

            // Skip entries pointing past end of file
            if ((long)offset + size > _packFile.Length)
                continue;

            _packedFiles.TryAdd(uid, new PackedFile { Size = size, Offset = offset });
        }
    }

    public void AddPackage(string fileName) => throw new NotSupportedException();

    public Stream LoadFile(string fileName)
    {
        var hash = HashFilename(fileName);
        if (!_packedFiles.TryGetValue(hash, out var file))
            throw new FileNotFoundException($"File not found in DNP: {fileName}");

        _packFile.Seek(file.Offset, SeekOrigin.Begin);
        var buffer = new byte[file.Size];
        _packFile.Read(buffer, 0, (int)file.Size);
        return new MemoryStream(buffer, writable: false);
    }

    public void Dispose()
    {
        _packedFiles.Clear();
        _packFile.Dispose();
    }

    /// <summary>
    /// Hashes a filename to its DNP unique ID (DNP.String2ID from CO2_CORE_DLL).
    /// DNP normalises paths with backslashes before hashing.
    /// </summary>
    private static uint HashFilename(string filename)
    {
        uint eax, ebx, edx, edi, esi;
        ulong num;

        // Normalise: lowercase + backslashes (DNP convention)
        var str = filename.ToLowerInvariant().Replace('/', '\\');

        // Build byte array, cast chars to byte (ASCII low-byte only)
        var bytes = new byte[str.Length];
        for (var i = 0; i < str.Length; i++)
            bytes[i] = (byte)str[i];

        // Pad to uint32 boundary
        var padded = new byte[bytes.Length + (bytes.Length % 4 != 0 ? 4 - bytes.Length % 4 : 0)];
        bytes.CopyTo(padded, 0);

        // Read as uint32 words
        var m = new uint[0x46];
        int wordCount = padded.Length / 4;
        using (var br = new BinaryReader(new MemoryStream(padded, writable: false)))
            for (var i = 0; i < wordCount; i++)
                m[i] = br.ReadUInt32();

        // Append two magic tail values
        m[wordCount++] = 0x9BE74448;
        m[wordCount++] = 0x66F42C48;

        uint v = 0xF4FA8928;
        edi = 0x7758B42B;
        esi = 0x37A8470E;

        for (uint ecx = 0; ecx < (uint)wordCount; ecx++)
        {
            ebx = 0x267B0B11;
            v   = (v << 1) | (v >> 0x1F);
            ebx ^= v;

            eax  = m[ecx];
            esi ^= eax;
            edi ^= eax;

            // --- First multiply: (ebx + edi) * esi ---
            edx  = ebx + edi;
            edx |= 0x02040801;
            edx &= 0xBFEF7FDF;
            num  = (ulong)edx * esi;
            eax  = (uint)num;
            edx  = (uint)(num >> 32);
            if (edx != 0) eax++;
            num  = (ulong)eax + edx;
            eax  = (uint)num;
            if ((uint)(num >> 32) != 0) eax++;
            // eax now holds the new esi value

            // --- Second edx uses OLD esi (before esi = eax) ---
            edx  = ebx + esi;   // <-- old esi still in register
            edx |= 0x00804021;
            edx &= 0x7DFEFBFF;

            esi  = eax;         // esi updated HERE, after second edx computed

            // --- Second multiply: edi * edx ---
            num  = (ulong)edi * edx;
            eax  = (uint)num;
            edx  = (uint)(num >> 32);
            num  = (ulong)edx + edx;   // 2 * high word
            edx  = (uint)num;
            if ((uint)(num >> 32) != 0) eax++;
            num  = (ulong)eax + edx;
            eax  = (uint)num;
            if ((uint)(num >> 32) != 0) eax += 2;
            edi  = eax;
        }

        return esi ^ edi;
    }
}
