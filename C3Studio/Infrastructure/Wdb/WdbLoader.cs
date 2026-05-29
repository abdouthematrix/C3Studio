using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace C3Studio.Infrastructure.Wdb;

/// <summary>
/// Reads a TQ Digital WDB package (magic "BDMG") and exposes its entries
/// as on-demand <see cref="MemoryStream"/> objects.
///
/// Ported from CptSky's cipher.cpp / WDB reader (C) 2013, rewritten in
/// safe, modern C# for the C3Studio infrastructure layer.
/// </summary>
public sealed class WdbLoader : IDisposable
{
    // ── Cipher ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 256-entry key table used by TQ's custom block cipher.
    /// Each WDB entry table block is decrypted independently with a fresh
    /// (a, b) accumulator, so entries can be processed in any order.
    /// </summary>
    private static ReadOnlySpan<uint> CipherKey => new uint[]
    {
        0x193aa698, 0x5496f7d5, 0x4208931b, 0x7a4106ec, 0x83e86840, 0xf49b6f8c,
        0xba3d9a51, 0x55f54ddd, 0x2de51372, 0x9afb571b, 0x3ab35406, 0xad64ff1f,
        0xc77764fe, 0x7f864466, 0x416d9cd4, 0xa2489278, 0xe30b86e4, 0x0b5231b6,
        0xba67aed6, 0xe5ab2467, 0x60028b90, 0x1d9e20c6, 0x2a7c692a, 0x6b691cdb,
        0x9e51f817, 0x9b763dec, 0x3d29323f, 0xcfe12b68, 0x754b459b, 0xa2238047,
        0xd9c55514, 0x6bdcffc1, 0x693e6340, 0x82383fe7, 0x1916ea5f, 0xec7bcd59,
        0x72de165a, 0xe79a1617, 0x8ec86234, 0xa8f0d284, 0x20c90226, 0x7bf98884,
        0x28a58331, 0x3ec3fa6e, 0x4ce0895b, 0xc353b4d0, 0x33ef064f, 0x21e5e210,
        0xc8bb589d, 0xe85dcab2, 0xac65829f, 0xa7bf92d0, 0x05a6174d, 0x25a50c2e,
        0xe5c78777, 0x3d75021f, 0x4baa9c98, 0x23bdc884, 0x9653bbd7, 0xbadce7f5,
        0xc283a484, 0xc040df2e, 0x9370a841, 0x2f316022, 0x36eed231, 0xac2cbc0c,
        0x13c0a49b, 0xcdd12997, 0x07fe91b2, 0xcd7eabcd, 0x2c01271d, 0x18432df8,
        0x599c6bc7, 0x75e93d5a, 0xb67a6ee2, 0x8e738e16, 0xff9073fd, 0xaf77026a,
        0xf86ea2fc, 0x91509ea3, 0x33a78dc6, 0x4f79234a, 0x3a7535bc, 0x3539fcb1,
        0x3103ee52, 0x4f6f1e69, 0x6bb3ebbc, 0x4cb77555, 0x8dd1e999, 0x2ade439d,
        0x11521fae, 0xb94d2545, 0x8dde9abd, 0x1909393f, 0xb792a23d, 0x749c455b,
        0xb5b60f2c, 0x380459ce, 0x0dad5820, 0xb130845b, 0x291cbd52, 0xde9a5bb7,
        0x51def961, 0x515b6408, 0xca6e823e, 0x382e6e74, 0xeebe3d71, 0x4c8f0c6a,
        0xe676dcea, 0x14e1dc7c, 0x6f7fc634, 0xcf85a943, 0xd39ea96e, 0x136e7c93,
        0x7164b304, 0xf32f1333, 0x35c34034, 0xde39d721, 0x91a87439, 0xc410111f,
        0x29f17aac, 0x1316a6ff, 0x12f194ee, 0x420b9499, 0xf72db0dc, 0x690b9f93,
        0x17d14bb2, 0x8f931ab8, 0x217500bc, 0x875413f8, 0x98b2e43d, 0xc51f9571,
        0x54cebdca, 0x0719cc79, 0xf3c7080d, 0xe4286771, 0xa3eab3cd, 0x4a6b00e0,
        0x11cf0759, 0x7e897379, 0x5b32876c, 0x5e8cd4f6, 0x0cedfa64, 0x919ac2c7,
        0xb214f3b3, 0x0e89c38c, 0xf0c43a39, 0xeae10522, 0x835bce06, 0x9eec43c2,
        0xea26a9d6, 0x69531821, 0x6725b24a, 0xda81b0e2, 0xd5b4ae33, 0x080f99fb,
        0x15a83daf, 0x29dfc720, 0x91e1900f, 0x28163d58, 0x83d107a2, 0x4eac149a,
        0x9f71da18, 0x61d5c4fa, 0xe3ab2a5f, 0xc7b0d63f, 0xb3cc752a, 0x61ebcfb6,
        0x26ffb52a, 0xed789e3f, 0xaa3bc958, 0x455a8788, 0xc9c082a9, 0x0a1bef0e,
        0xc29a5a7e, 0x150d4735, 0x943809e0, 0x69215510, 0xef0b0da9, 0x3b4e9fb3,
        0xd8b5d04c, 0xc7a023a8, 0xb0d50288, 0x64821375, 0xc260e8cf, 0x8496bd2c,
        0xff4f5435, 0x0fb5560c, 0x7cd74a52, 0x93589c80, 0x88975c47, 0x83bda89d,
        0x8bcc4296, 0x01b82c21, 0xfd821dbf, 0x26520b47, 0x04983e19, 0xd3e1ca27,
        0x782c580f, 0x326ff573, 0xc157bcc7, 0x4f5e6b84, 0x44ebfbfb, 0xda26d9d8,
        0x6cd9d08e, 0x1719f1d8, 0x715c0487, 0x2c2d3c92, 0x53faaba9, 0xbc836146,
        0x510c92d6, 0xe089f82a, 0x4680171f, 0x369f00de, 0x70ec2331, 0x0e253d55,
        0xdafb9717, 0xe5dd922d, 0x95915d21, 0xa0202f96, 0xa161cc47, 0xeacfa6f1,
        0xed5e9189, 0xdab87684, 0xa4b76d4a, 0xfa704897, 0x631f10ba, 0xd39da8f9,
        0x5db4c0e4, 0x16fde42a, 0x2dff7580, 0xb56fec7e, 0xc3ffb370, 0x8e6f36bc,
        0x6097d459, 0x514d5d36, 0xa5a737e2, 0x3977b9b3, 0xfd31a0ca, 0x903368db,
        0xe8370d61, 0x98109520, 0xade23cac, 0x99f82e04, 0x41de7ea3, 0x84a1c295,
        0x09191be0, 0x30930d02, 0x1c9fa44a, 0xc406b6d7, 0xeedca152, 0x6149809c,
        0xb0099ef4, 0xc5f653a5, 0x4c10790d, 0x7303286c,
    };

    /// <summary>
    /// Decrypts a single WDB entry block in-place.
    /// <paramref name="length"/> must be a multiple of 4.
    /// </summary>
    private static void CipherDecrypt(byte[] buf, int offset, int length)
    {
        if (length % 4 != 0)
            throw new ArgumentException("Entry size must be a multiple of 4.", nameof(length));

        uint a = 0xEFFEAABB;
        uint b = 0xEEEEEEEE;
        ReadOnlySpan<uint> key = CipherKey;

        int dwords = length / 4;
        for (int i = 0; i < dwords; i++)
        {
            int pos = offset + i * 4;
            uint word = BitConverter.ToUInt32(buf, pos);

            b += key[(int)(a & 0xFF)];
            uint dec = (b + a) ^ word;

            buf[pos]     = (byte)dec;
            buf[pos + 1] = (byte)(dec >> 8);
            buf[pos + 2] = (byte)(dec >> 16);
            buf[pos + 3] = (byte)(dec >> 24);

            a = (a >> 11) | (~a << 0x15) + 0x11111111;
            b += dec + (b << 5) + 3;
        }
    }

    // ── WDB structures ────────────────────────────────────────────────────────

    private const string Magic = "BDMG";

    /// <summary>
    /// 16-byte WDB file header (little-endian).
    /// </summary>
    private readonly record struct WdbHeader(int TotalSize, int EntryTableOffset, uint EntryCount);

    /// <summary>
    /// One 84-byte entry in the (decrypted) entry table.
    /// </summary>
    public sealed class WdbEntry
    {
        public uint   Hash   { get; init; }
        public int    Offset { get; init; }
        public int    Size   { get; init; }
        // 8 padding bytes skipped
        public string Name   { get; init; } = string.Empty;

        internal const int BinarySize = 4 + 4 + 4 + 8 + 64; // = 84
    }

    // ── Fields ────────────────────────────────────────────────────────────────

    private FileStream? _stream;
    private readonly Dictionary<string, WdbEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All entries in the open archive, keyed case-insensitively by name.</summary>
    public IReadOnlyDictionary<string, WdbEntry> Entries => _entries;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Open a WDB package for reading.</summary>
    public void Open(string path)
    {
        Close();
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        ReadHeader();
        ReadEntries();
    }

    /// <summary>
    /// Returns a fresh <see cref="MemoryStream"/> (position 0) for the named entry,
    /// or <c>null</c> if the entry does not exist.
    /// The caller owns the stream.
    /// </summary>
    public MemoryStream? OpenEntry(string name)
    {
        if (!_entries.TryGetValue(name, out var entry))
            return null;
        return ExtractEntry(entry);
    }

    /// <summary>
    /// Returns a <see cref="StreamReader"/> for the named entry decoded as
    /// Windows-1252 text, or <c>null</c> if the entry does not exist.
    /// The caller owns the reader (and the underlying stream).
    /// </summary>
    public StreamReader? OpenTextEntry(string name)
    {
        var ms = OpenEntry(name);
        return ms is null ? null
            : new StreamReader(ms, Encoding.Latin1, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: false);
    }

    /// <summary>Close the file and release all resources.</summary>
    public void Close()
    {
        _stream?.Dispose();
        _stream = null;
        _entries.Clear();
    }

    public void Dispose() => Close();

    // ── Private helpers ───────────────────────────────────────────────────────

    private void ReadHeader()
    {
        Span<byte> buf = stackalloc byte[16];
        _stream!.ReadExactly(buf);

        string magic = Encoding.ASCII.GetString(buf[..4]);
        if (magic != Magic)
            throw new InvalidDataException($"Not a WDB file (expected '{Magic}', got '{magic}').");

        int totalSize       = BitConverter.ToInt32(buf[4..8]);
        int entryTableOff   = BitConverter.ToInt32(buf[8..12]);
        uint entryCount     = BitConverter.ToUInt32(buf[12..16]);

        _ = new WdbHeader(totalSize, entryTableOff, entryCount); // validation only
        _stream.Seek(entryTableOff, SeekOrigin.Begin);

        // Store count for use in ReadEntries
        _pendingCount = entryCount;
    }

    private uint _pendingCount;

    private void ReadEntries()
    {
        int entrySize  = WdbEntry.BinarySize; // 84
        int totalBytes = (int)_pendingCount * entrySize;
        byte[] data    = new byte[totalBytes];

        _stream!.ReadExactly(data);

        // Each 84-byte block is independently encrypted
        for (uint i = 0; i < _pendingCount; i++)
            CipherDecrypt(data, (int)(i * entrySize), entrySize);

        for (uint i = 0; i < _pendingCount; i++)
        {
            var entry = ParseEntry(data, (int)(i * entrySize));
            _entries.TryAdd(entry.Name, entry);   // first occurrence wins
        }
    }

    private static WdbEntry ParseEntry(byte[] buf, int start)
    {
        uint hash   = BitConverter.ToUInt32(buf, start);
        int  offset = BitConverter.ToInt32(buf,  start + 4);
        int  size   = BitConverter.ToInt32(buf,  start + 8);
        // 8 padding bytes at start+12 are ignored

        int nameStart = start + 20;
        int nameLen   = 0;
        while (nameLen < 64 && buf[nameStart + nameLen] != 0)
            nameLen++;
        string name = Encoding.ASCII.GetString(buf, nameStart, nameLen);

        return new WdbEntry { Hash = hash, Offset = offset, Size = size, Name = name };
    }

    private MemoryStream ExtractEntry(WdbEntry entry)
    {
        _stream!.Seek(entry.Offset, SeekOrigin.Begin);

        var ms     = new MemoryStream(entry.Size);
        byte[] buf = new byte[4096];
        int remaining = entry.Size;

        while (remaining > 0)
        {
            int toRead = Math.Min(remaining, buf.Length);
            int read   = _stream.Read(buf, 0, toRead);
            if (read == 0) throw new EndOfStreamException("Unexpected EOF extracting WDB entry.");
            ms.Write(buf, 0, read);
            remaining -= read;
        }

        ms.Position = 0;
        return ms;
    }
}
