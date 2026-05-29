using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace C3Studio.Infrastructure.Wdb;

/// <summary>
/// Maps WDB entry names to DBC formats, disambiguating cases where two
/// formats share the same binary magic (RSDB_SMALL vs RSDB_BIG).
/// </summary>
internal static class WdbEntryMap
{
    // Key  = WDB entry name (lower-case, no extension)
    // Value = the correct DbcFormat to use when converting that entry
    private static readonly Dictionary<string, DbcFormat> s_map =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // ── RSDB_BIG (uint64 key) ──────────────────────────────────────
            ["3dmotion"]      = DbcFormat.RSDB_BIG,
            ["armetmotion"]   = DbcFormat.RSDB_BIG,
            ["armormotion"]   = DbcFormat.RSDB_BIG,   // alternate name seen in some builds
            ["capemotion"]    = DbcFormat.RSDB_BIG,
            ["headmotion"]    = DbcFormat.RSDB_BIG,
            ["miscmotion"]    = DbcFormat.RSDB_BIG,
            ["mountmotion"]   = DbcFormat.RSDB_BIG,
            ["weaponmotion"]  = DbcFormat.RSDB_BIG,
            ["pelvismotion"]  = DbcFormat.RSDB_BIG,
            ["spiritmotion"]  = DbcFormat.RSDB_BIG,

            // ── RSDB_SMALL (uint32 key) ────────────────────────────────────
            ["3dobj"]         = DbcFormat.RSDB_SMALL,
            ["3deffectobj"]   = DbcFormat.RSDB_SMALL,

            // ── RSDC (uint64 key, different magic) ─────────────────────────
            ["3dtexture"]     = DbcFormat.RSDC,

            // ── EFFE ───────────────────────────────────────────────────────
            ["3deffect"]      = DbcFormat.EFFE,

            // ── FF32 ───────────────────────────────────────────────────────
            ["3deffect2"]     = DbcFormat.FF32,

            // ── SIMO ───────────────────────────────────────────────────────
            ["3dsimpleobj"]   = DbcFormat.SIMO,

            // ── MESZ (extended role parts) ─────────────────────────────────
            ["armor"]         = DbcFormat.MESZ,
            ["armet"]         = DbcFormat.MESZ,
            ["weapon"]        = DbcFormat.MESZ,
            ["mount"]         = DbcFormat.MESZ,
            ["cape"]          = DbcFormat.MESZ,
            ["head"]          = DbcFormat.MESZ,
            ["misc"]          = DbcFormat.MESZ,
            ["pelvis"]        = DbcFormat.MESZ,
            ["spirit"]        = DbcFormat.MESZ,
        };

    /// <summary>
    /// Returns the correct <see cref="DbcFormat"/> for the given WDB entry name,
    /// or <see cref="DbcFormat.Unknown"/> if the name is not recognised.
    /// </summary>
    public static DbcFormat Resolve(string entryName)
    {
        // Strip path prefix ("ini/3dobj" → "3dobj") and extension (".dbc")
        string stem = Path.GetFileNameWithoutExtension(entryName);
        return s_map.TryGetValue(stem, out var fmt) ? fmt : DbcFormat.Unknown;
    }
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Provides INI text for a named game-data file by trying sources in priority order:
/// <list type="number">
///   <item>A DBC entry inside the open <see cref="WdbLoader"/> (converted on-the-fly).</item>
///   <item>A plain <c>.ini</c> file on disk.</item>
/// </list>
/// The returned text is always in the <c>key=value</c> or section format that the
/// existing C3Studio parsers (<c>ResIniParser</c>, <c>NpcIniParser</c>, …) already consume.
/// </summary>
public sealed class WdbIniSource : IDisposable
{
    public readonly WdbLoader? _wdb;

    /// <param name="wdbPath">
    /// Full path to <c>c3.wdb</c>.  Pass <c>null</c> to skip WDB and always fall
    /// back to plain files.
    /// </param>
    public WdbIniSource(string? wdbPath)
    {
        if (!string.IsNullOrEmpty(wdbPath) && File.Exists(wdbPath))
        {
            _wdb = new WdbLoader();
            _wdb.Open(wdbPath);
        }
    }

    /// <summary>
    /// Returns a <see cref="StreamReader"/> for <paramref name="plainIniPath"/>.
    ///
    /// Resolution order:
    /// <list type="number">
    ///   <item>
    ///     WDB entry "<c>ini/{filename}.dbc</c>" (also tried without the <c>ini/</c>
    ///     prefix and with <c>.dbc</c> vs no extension) → converted to INI text.
    ///   </item>
    ///   <item>Plain file at <paramref name="plainIniPath"/>.</item>
    /// </list>
    /// Returns <c>null</c> if neither source exists.
    /// </summary>
    public StreamReader? OpenIni(string plainIniPath)
    {
        if (_wdb is not null)
        {
            var reader = TryOpenFromWdb(plainIniPath);
            if (reader is not null) return reader;
        }

        if (File.Exists(plainIniPath))
            return new StreamReader(plainIniPath, Encoding.Latin1);

        return null;
    }

    /// <summary>
    /// Convenience: returns the full INI text as a string, or <c>null</c>.
    /// </summary>
    public string? ReadAllIni(string plainIniPath)
    {
        using var reader = OpenIni(plainIniPath);
        return reader?.ReadToEnd();
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    private StreamReader? TryOpenFromWdb(string plainIniPath)
    {
        string fileName = Path.GetFileNameWithoutExtension(plainIniPath); // e.g. "3dobj"

        // Candidate WDB entry names to probe (the WDB may store them as
        // "ini/3dobj.dbc", "3dobj.dbc", "ini/3dobj", or just "3dobj")
        Span<string> candidates = [
            $"ini/{fileName}.dbc",
            $"{fileName}.dbc",
            $"ini/{fileName}",
            fileName,
        ];

        DbcFormat format = WdbEntryMap.Resolve(fileName);

        foreach (var candidate in candidates)
        {
            using var ms = _wdb!.OpenEntry(candidate);
            if (ms is null) continue;

            // If format is unknown, try to auto-detect from the stream magic
            DbcFormat fmt = format == DbcFormat.Unknown
                ? DbcReader.DetectFormat(ms)
                : format;

            if (fmt == DbcFormat.Unknown) continue;

            string? ini = DbcReader.ToIni(ms, fmt);
            if (ini is null) continue;

            return new StreamReader(new MemoryStream(Encoding.Latin1.GetBytes(ini)),
                                    Encoding.Latin1,
                                    detectEncodingFromByteOrderMarks: false,
                                    bufferSize: 4096,
                                    leaveOpen: false);
        }

        return null;
    }

    public void Dispose() => _wdb?.Dispose();
}
