using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace C3Studio.Infrastructure.Wdb;

// ─────────────────────────────────────────────────────────────────────────────
// DbcFormat
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The binary format tag found at offset 0 in a DBC file.
/// </summary>
public enum DbcFormat
{
    Unknown,
    /// <summary>3DEffect  — EFFE 0x45464645</summary>
    EFFE,
    /// <summary>3DEffect2 — FF32 0x32334546</summary>
    FF32,
    /// <summary>3DObj / 3DEffectObj — RSDB with uint32 keys — 0x42445352</summary>
    RSDB_SMALL,
    /// <summary>3DMotion / WeaponMotion / MountMotion — RSDB with uint64 keys — 0x42445352</summary>
    RSDB_BIG,
    /// <summary>3DTexture — RSDC 0x43445352</summary>
    RSDC,
    /// <summary>3DSimpleObj — SIMO 0x4F4D4953</summary>
    SIMO,
    /// <summary>Armet/Armor/Weapon/etc (extended) — MESZ 0x5A53454D</summary>
    MESZ,
    /// <summary>Armet/Armor/Weapon/etc (classic)  — MESH 0x4853454D</summary>
    MESH,
    /// <summary>EmotionIco — EMOI 0x494F4D45</summary>
    EMOI,
    /// <summary>Material   — MATR 0x5254414D</summary>
    MATR,
    /// <summary>RolePart   — ROPT 0x54504F52</summary>
    ROPT,
}

// ─────────────────────────────────────────────────────────────────────────────
// DbcReader — static façade
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Reads TQ Digital DBC binary files and converts them to the plain-text
/// INI format that C3Studio's existing parsers already consume.
///
/// All methods operate on a <see cref="Stream"/> so they work identically
/// whether the data comes from a <see cref="MemoryStream"/> extracted out of
/// a WDB package or from a raw file on disk.
/// </summary>
public static class DbcReader
{
    private const int MAX_NAME = 0x20;   // 32 bytes, null-terminated

    // ── Format detection ─────────────────────────────────────────────────────

    /// <summary>
    /// Peeks the 4-byte magic at position 0 and maps it to a <see cref="DbcFormat"/>.
    /// Stream position is restored afterwards.
    /// </summary>
    public static DbcFormat DetectFormat(Stream s)
    {
        long saved = s.Position;
        try
        {
            Span<byte> magic = stackalloc byte[4];
            if (s.Read(magic) < 4) return DbcFormat.Unknown;
            uint tag = BitConverter.ToUInt32(magic);
            return tag switch
            {
                0x45464645 => DbcFormat.EFFE,
                0x32334546 => DbcFormat.FF32,
                0x4F4D4953 => DbcFormat.SIMO,
                0x5A53454D => DbcFormat.MESZ,
                0x4853454D => DbcFormat.MESH,
                0x494F4D45 => DbcFormat.EMOI,
                0x5254414D => DbcFormat.MATR,
                0x54504F52 => DbcFormat.ROPT,
                0x43445352 => DbcFormat.RSDC,
                // RSDB_SMALL and RSDB_BIG share the same magic — disambiguation
                // must be done by the caller (file name context)
                0x42445352 => DbcFormat.RSDB_SMALL,
                _          => DbcFormat.Unknown,
            };
        }
        finally { s.Position = saved; }
    }

    // ── Public entry points ───────────────────────────────────────────────────

    /// <summary>
    /// Converts a DBC stream to a plain-text INI string using the explicit
    /// <paramref name="format"/>. Returns the INI text or <c>null</c> on failure.
    /// </summary>
    public static string? ToIni(Stream stream, DbcFormat format)
    {
        return format switch
        {
            DbcFormat.EFFE       => EffeToIni(stream),
            DbcFormat.FF32       => Ff32ToIni(stream),
            DbcFormat.RSDB_SMALL => RsdbSmallToIni(stream),
            DbcFormat.RSDB_BIG   => RsdbBigToIni(stream),
            DbcFormat.RSDC       => RsdcToIni(stream),
            DbcFormat.SIMO       => SimoToIni(stream),
            DbcFormat.MESZ       => MeszToIni(stream),
            DbcFormat.MESH       => MeshToIni(stream),
            DbcFormat.EMOI       => EmoiToIni(stream),
            DbcFormat.MATR       => MatrToIni(stream),
            DbcFormat.ROPT       => RoptToIni(stream),
            _                    => null,
        };
    }

    // ── Low-level stream helpers ──────────────────────────────────────────────

    private static BinaryReader OpenReader(Stream s) =>
        new(s, Encoding.Latin1, leaveOpen: true);

    private static string ReadFixedString(BinaryReader r, int maxBytes)
    {
        byte[] raw = r.ReadBytes(maxBytes);
        int len = Array.IndexOf(raw, (byte)0);
        if (len < 0) len = maxBytes;
        return Encoding.Latin1.GetString(raw, 0, len);
    }

    /// <summary>Read a null-terminated string from the current position.</summary>
    private static string ReadNullTerminated(Stream s)
    {
        var sb = new StringBuilder(64);
        int b;
        while ((b = s.ReadByte()) > 0) sb.Append((char)b);
        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // RSDB_SMALL  (3DObj, 3DEffectObj) — uint32 key
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Produces <c>id=path</c> lines compatible with <c>ResIniParser</c>.
    /// Key type: uint32.
    /// </summary>
    public static string? RsdbSmallToIni(Stream s)
    {
        using var r = OpenReader(s);
        if (r.ReadUInt32() != 0x42445352) return null; // RSDB
        int amount = r.ReadInt32();

        // Entry table: [uint32 id | uint32 pathOffset]  per entry
        // Path data follows the entire entry table.
        long tableStart = s.Position;
        const int entryFixed = 4 + 4; // id + offset = 8 bytes (no padding for SMALL)

        var ids     = new uint[amount];
        var offsets = new uint[amount];
        for (int i = 0; i < amount; i++)
        {
            ids[i]     = r.ReadUInt32();
            offsets[i] = r.ReadUInt32();
        }

        var sb = new StringBuilder(amount * 48);
        for (int i = 0; i < amount; i++)
        {
            s.Seek(offsets[i], SeekOrigin.Begin);
            string path = ReadNullTerminated(s);
            if (!string.IsNullOrEmpty(path))
                sb.AppendLine($"{ids[i]}={path}");
        }
        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // RSDB_BIG  (3DMotion, WeaponMotion, MountMotion …) — uint64 key
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Produces <c>id=path</c> lines compatible with <c>ResIniParser</c>.
    /// Key type: uint64.
    /// </summary>
    public static string? RsdbBigToIni(Stream s)
    {
        using var r = OpenReader(s);
        if (r.ReadUInt32() != 0x42445352) return null;
        int amount = r.ReadInt32();

        var ids     = new ulong[amount];
        var offsets = new uint[amount];
        for (int i = 0; i < amount; i++)
        {
            ids[i]     = r.ReadUInt64();
            offsets[i] = r.ReadUInt32();
        }

        var sb = new StringBuilder(amount * 56);
        for (int i = 0; i < amount; i++)
        {
            s.Seek(offsets[i], SeekOrigin.Begin);
            string path = ReadNullTerminated(s);
            if (!string.IsNullOrEmpty(path))
                sb.AppendLine($"{ids[i]}={path}");
        }
        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // RSDC  (3DTexture) — uint64 key, same layout as RSDB_BIG
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Produces <c>id=path</c> lines.  Identical layout to RSDB_BIG but magic
    /// is <c>RSDC</c> (0x43445352).
    /// </summary>
    public static string? RsdcToIni(Stream s)
    {
        using var r = OpenReader(s);
        if (r.ReadUInt32() != 0x43445352) return null; // RSDC
        int amount = r.ReadInt32();

        var ids     = new ulong[amount];
        var offsets = new uint[amount];
        for (int i = 0; i < amount; i++)
        {
            ids[i]     = r.ReadUInt64();
            offsets[i] = r.ReadUInt32();
        }

        var sb = new StringBuilder(amount * 56);
        for (int i = 0; i < amount; i++)
        {
            s.Seek(offsets[i], SeekOrigin.Begin);
            string path = ReadNullTerminated(s);
            if (!string.IsNullOrEmpty(path))
                sb.AppendLine($"{ids[i]}={path}");
        }
        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SIMO  (3DSimpleObj)
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Produces section-based INI matching the SIMO plain format:
    /// <code>
    /// [ObjIDType{id}]
    /// PartAmount={n}
    /// Part0={meshId}
    /// Texture0={texId}
    /// …
    /// </code>
    /// </summary>
    public static string? SimoToIni(Stream s)
    {
        using var r = OpenReader(s);
        if (r.ReadUInt32() != 0x4F4D4953) return null; // SIMO
        int amount = r.ReadInt32();

        var sb = new StringBuilder(amount * 64);
        for (int i = 0; i < amount; i++)
        {
            int uniqId    = r.ReadInt32();
            int partCount = r.ReadInt32();

            sb.AppendLine($"[ObjIDType{uniqId}]");
            sb.AppendLine($"PartAmount={partCount}");
            for (int p = 0; p < partCount; p++)
            {
                int partId  = r.ReadInt32();
                int texture = r.ReadInt32();
                sb.AppendLine($"Part{p}={partId}");
                sb.AppendLine($"Texture{p}={texture}");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // EFFE  (3DEffect)
    // ══════════════════════════════════════════════════════════════════════════

    // Entry fixed header: Name[32] + Amount(i16) + Delay(i32) + LoopTime + FrameInterval
    //   + LoopInterval + OffsetX + OffsetY + OffsetZ + Unknown1(u8) + ColorEnable(u8)
    //   + Level(u8) + Unknown2(u8)   → 32+2+4*8+4 = 32+2+32+4 = 70 bytes (before Parts)
    // Each Part: EffectId(i32) + TextureId(i32) + Unknown1(i32) + Asb(u8) + Adb(u8) + Unknown2(i16)
    //   = 4+4+4+1+1+2 = 16 bytes

    public static string? EffeToIni(Stream s)
    {
        using var r = OpenReader(s);
        if (r.ReadUInt32() != 0x45464645) return null; // EFFE

        int amount = r.ReadInt32();
        var sb = new StringBuilder(amount * 128);

        for (int i = 0; i < amount; i++)
        {
            string name      = ReadFixedString(r, MAX_NAME);
            short  partCount = r.ReadInt16();
            int    delay     = r.ReadInt32();
            int    loopTime  = r.ReadInt32();
            int    frameInt  = r.ReadInt32();
            int    loopInt   = r.ReadInt32();
            int    offX      = r.ReadInt32();
            int    offY      = r.ReadInt32();
            int    offZ      = r.ReadInt32();
            r.ReadByte();                          // Unknown1
            byte colorEnable = r.ReadByte();
            byte level       = r.ReadByte();
            r.ReadByte();                          // Unknown2

            sb.AppendLine($"[{name}]");
            sb.AppendLine($"Amount={partCount}");
            for (int p = 0; p < partCount; p++)
            {
                int  effectId  = r.ReadInt32();
                int  textureId = r.ReadInt32();
                r.ReadInt32();                     // Unknown1
                byte asb       = r.ReadByte();
                byte adb       = r.ReadByte();
                r.ReadInt16();                     // Unknown2

                sb.AppendLine($"EffectId{p}={effectId}");
                sb.AppendLine($"TextureId{p}={textureId}");
                sb.AppendLine($"Asb{p}={asb}");
                sb.AppendLine($"Adb{p}={adb}");
            }
            sb.AppendLine($"Delay={delay}");
            sb.AppendLine($"LoopTime={loopTime}");
            sb.AppendLine($"FrameInterval={frameInt}");
            sb.AppendLine($"LoopInterval={loopInt}");
            sb.AppendLine($"OffsetX={offX}");
            sb.AppendLine($"OffsetY={offY}");
            sb.AppendLine($"OffsetZ={offZ}");
            sb.AppendLine($"ColorEnable={colorEnable}");
            sb.AppendLine($"Lev={level}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // FF32  (3DEffect2) — extended Part struct with many unknowns
    // ══════════════════════════════════════════════════════════════════════════
    // Each Part: EffectId(i32)+TextureId(i32)+Unknown1(i64)+Unknown2(i32)+Unknown3(i32)
    //   +Unknown4(i64)+Unknown5(i32)+Asb(u8)+Adb(u8)+Unknown6(i16)+Unknown7(i32)
    //   +Unknown8(i32)+Unknown9(i32)+Unknown10-13(i64 × 4)
    //   = 4+4+8+4+4+8+4+1+1+2+4+4+4+32 = 84 bytes

    public static string? Ff32ToIni(Stream s)
    {
        using var r = OpenReader(s);
        if (r.ReadUInt32() != 0x32334546) return null; // FF32

        int amount = r.ReadInt32();
        var sb = new StringBuilder(amount * 128);

        for (int i = 0; i < amount; i++)
        {
            string name      = ReadFixedString(r, MAX_NAME);
            short  partCount = r.ReadInt16();
            int    delay     = r.ReadInt32();
            int    loopTime  = r.ReadInt32();
            int    frameInt  = r.ReadInt32();
            int    loopInt   = r.ReadInt32();
            int    offX      = r.ReadInt32();
            int    offY      = r.ReadInt32();
            int    offZ      = r.ReadInt32();
            r.ReadByte();
            byte colorEnable = r.ReadByte();
            byte level       = r.ReadByte();
            r.ReadByte();

            sb.AppendLine($"[{name}]");
            sb.AppendLine($"Amount={partCount}");
            for (int p = 0; p < partCount; p++)
            {
                int  effectId  = r.ReadInt32();
                int  textureId = r.ReadInt32();
                r.ReadInt64();  r.ReadInt32();  r.ReadInt32();  // Unk1,2,3
                r.ReadInt64();  r.ReadInt32();                   // Unk4,5
                byte asb       = r.ReadByte();
                byte adb       = r.ReadByte();
                r.ReadInt16();  r.ReadInt32();  r.ReadInt32();  r.ReadInt32(); // Unk6-9
                r.ReadInt64();  r.ReadInt64();  r.ReadInt64();  r.ReadInt64(); // Unk10-13

                sb.AppendLine($"EffectId{p}={effectId}");
                sb.AppendLine($"TextureId{p}={textureId}");
                sb.AppendLine($"Asb{p}={asb}");
                sb.AppendLine($"Adb{p}={adb}");
            }
            sb.AppendLine($"Delay={delay}");
            sb.AppendLine($"LoopTime={loopTime}");
            sb.AppendLine($"FrameInterval={frameInt}");
            sb.AppendLine($"LoopInterval={loopInt}");
            sb.AppendLine($"OffsetX={offX}");
            sb.AppendLine($"OffsetY={offY}");
            sb.AppendLine($"OffsetZ={offZ}");
            sb.AppendLine($"ColorEnable={colorEnable}");
            sb.AppendLine($"Lev={level}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // MESH  (Armet/Armor/Weapon/Mount/etc — classic)
    // ══════════════════════════════════════════════════════════════════════════
    // Part: Mesh(i32)+Texture(i32)+MixTex(i32)+MixOpt(u8)+Asb(u8)+Adb(u8)+Material(u8) = 16

    public static string? MeshToIni(Stream s)
    {
        using var r = OpenReader(s);
        if (r.ReadUInt32() != 0x4853454D) return null; // MESH

        int amount = r.ReadInt32();
        var sb = new StringBuilder(amount * 80);

        for (int i = 0; i < amount; i++)
        {
            int uniqId    = r.ReadInt32();
            int partCount = r.ReadInt32();

            sb.AppendLine($"[{uniqId}]");
            sb.AppendLine($"Part={partCount}");
            for (int p = 0; p < partCount; p++)
            {
                int  mesh     = r.ReadInt32();
                int  texture  = r.ReadInt32();
                int  mixTex   = r.ReadInt32();
                byte mixOpt   = r.ReadByte();
                byte asb      = r.ReadByte();
                byte adb      = r.ReadByte();
                byte material = r.ReadByte();

                sb.AppendLine($"Mesh{p}={mesh}");
                sb.AppendLine($"Texture{p}={texture}");
                sb.AppendLine($"MixTex{p}={mixTex}");
                sb.AppendLine($"MixOpt{p}={mixOpt}");
                sb.AppendLine($"Asb{p}={asb}");
                sb.AppendLine($"Adb{p}={adb}");
                sb.AppendLine($"Material{p}={( material == 0 ? "default" : material.ToString() )}");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // MESZ  (Armet/Armor/Weapon/Mount/etc — extended, 8 extra unknowns)
    // ══════════════════════════════════════════════════════════════════════════
    // Part: Mesh+Texture+MixTex(i32×3)+MixOpt+Asb+Adb+Material(u8×4)+Unk1-8(i32×8) = 48

    public static string? MeszToIni(Stream s)
    {
        using var r = OpenReader(s);
        if (r.ReadUInt32() != 0x5A53454D) return null; // MESZ

        int amount = r.ReadInt32();
        var sb = new StringBuilder(amount * 80);

        for (int i = 0; i < amount; i++)
        {
            int uniqId    = r.ReadInt32();
            int partCount = r.ReadInt32();

            sb.AppendLine($"[{uniqId}]");
            sb.AppendLine($"Part={partCount}");
            for (int p = 0; p < partCount; p++)
            {
                int  mesh     = r.ReadInt32();
                int  texture  = r.ReadInt32();
                int  mixTex   = r.ReadInt32();
                byte mixOpt   = r.ReadByte();
                byte asb      = r.ReadByte();
                byte adb      = r.ReadByte();
                byte material = r.ReadByte();
                // 8 unknown int32s — skip
                for (int u = 0; u < 8; u++) r.ReadInt32();

                sb.AppendLine($"Mesh{p}={mesh}");
                sb.AppendLine($"Texture{p}={texture}");
                sb.AppendLine($"MixTex{p}={mixTex}");
                sb.AppendLine($"MixOpt{p}={mixOpt}");
                sb.AppendLine($"Asb{p}={asb}");
                sb.AppendLine($"Adb{p}={adb}");
                sb.AppendLine($"Material{p}={( material == 0 ? "default" : material.ToString() )}");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // EMOI  (EmotionIco)
    // ══════════════════════════════════════════════════════════════════════════
    // Entry: ID(i32) + Name[32]  = 36 bytes

    public static string? EmoiToIni(Stream s)
    {
        using var r = OpenReader(s);
        if (r.ReadUInt32() != 0x494F4D45) return null; // EMOI

        int amount = r.ReadInt32();
        var sb = new StringBuilder(amount * 40);

        for (int i = 0; i < amount; i++)
        {
            int    id   = r.ReadInt32();
            string name = ReadFixedString(r, MAX_NAME);
            sb.AppendLine($"{id} {name}");
        }
        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // MATR  (Material)
    // ══════════════════════════════════════════════════════════════════════════
    // Entry: Name[32] + Param0-4 (uint32 × 5)  = 52 bytes

    public static string? MatrToIni(Stream s)
    {
        using var r = OpenReader(s);
        if (r.ReadUInt32() != 0x5254414D) return null; // MATR

        int amount = r.ReadInt32();
        var sb = new StringBuilder(amount * 64);
        sb.AppendLine($"material={amount}");

        for (int i = 0; i < amount; i++)
        {
            string name   = ReadFixedString(r, MAX_NAME);
            uint   param0 = r.ReadUInt32();
            uint   param1 = r.ReadUInt32();
            uint   param2 = r.ReadUInt32();
            uint   param3 = r.ReadUInt32();
            uint   param4 = r.ReadUInt32();
            sb.AppendLine($"{name} {param0:X2} {param1:X2} {param2:X2} {param3:X2} {param4:X2}");
        }
        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ROPT  (RolePart)
    // ══════════════════════════════════════════════════════════════════════════
    // Header: Identifier(i32) + PartAmount(i32) + DumyAmount(i32)
    // Part:   Name[32] + MeshIni[256] + MotionIni[256]  = 544 bytes
    // Dumy:   UniqId(i32) + Name[32]  = 36 bytes

    public static string? RoptToIni(Stream s)
    {
        using var r = OpenReader(s);
        if (r.ReadUInt32() != 0x54504F52) return null; // ROPT

        int partAmount = r.ReadInt32();
        int dumyAmount = r.ReadInt32();

        var sb = new StringBuilder();
        sb.AppendLine("[Config]");
        sb.AppendLine($"Count={partAmount}");

        for (int i = 0; i < partAmount; i++)
        {
            string partName  = ReadFixedString(r, MAX_NAME);
            string meshIni   = ReadFixedString(r, 256);
            string motionIni = ReadFixedString(r, 256);
            sb.AppendLine($"Part{i}={partName}");
            sb.AppendLine($"MeshIni{i}={meshIni}");
            sb.AppendLine($"MotionIni{i}={motionIni}");
        }
        sb.AppendLine();
        sb.AppendLine("[Dumy]");
        // The original writer appended garbage chars after the count — we emit clean output
        sb.AppendLine($"Count={dumyAmount}");

        for (int i = 0; i < dumyAmount; i++)
        {
            r.ReadInt32();                         // UniqId (== i)
            string dumyName = ReadFixedString(r, MAX_NAME);
            sb.AppendLine($"Dumy{i}={dumyName}");
        }
        return sb.ToString();
    }
}
