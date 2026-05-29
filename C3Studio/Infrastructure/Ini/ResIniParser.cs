using System;
using System.Collections.Generic;
using System.IO;

namespace C3Studio.Infrastructure.Ini;

/// <summary>
/// Parses the shared <c>id=filepath</c> format used by 3dobj.ini, 3dtexture.ini,
/// 3dmotion.ini, WeaponMotion.ini, MountMotion.ini, etc.
///
/// Keys are <see cref="ulong"/> — motion IDs such as 9990010100 exceed uint32 max.
///
/// <para>
/// The overload that accepts a <see cref="TextReader"/> is used when the data is
/// sourced from a WDB-extracted DBC (converted to INI text on-the-fly by
/// <c>DbcReader</c>) rather than from a plain file on disk.
/// </para>
/// </summary>
public static class ResIniParser
{
    // ── File-path overload (original behaviour, unchanged) ────────────────────

    public static Dictionary<ulong, string> Parse(string filePath)
    {
        if (!File.Exists(filePath)) return new();
        using var reader = new StreamReader(filePath);
        return Parse(reader);
    }

    // ── TextReader overload (new — used by WdbIniSource) ─────────────────────

    public static Dictionary<ulong, string> Parse(TextReader reader)
    {
        var map = new Dictionary<ulong, string>();

        string? rawLine;
        while ((rawLine = reader.ReadLine()) is not null)
        {
            var line = rawLine.AsSpan().Trim();
            if (line.IsEmpty || line.StartsWith("//")) continue;

            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            if (ulong.TryParse(line[..eq].Trim(), out ulong id))
            {
                var path = line[(eq + 1)..].Trim().ToString();
                if (!string.IsNullOrEmpty(path))
                    map[id] = path;
            }
        }
        return map;
    }
}
