using C3Studio.Core.Models;
using System.IO;

namespace C3Studio.Infrastructure.Ini;

/// <summary>
/// Parses <c>ini/Mount.ini</c>. Two section-header formats coexist in the wild:
/// <list type="bullet">
///   <item>
///     <b>Old format</b> — used by <c>C3DRoleData::CreateRolePartInfo</c>:<br/>
///     <c>[Mount410301]</c> with bare <c>Mesh=</c> / <c>Texture=</c> keys and an
///     optional <c>Texture2=</c> followed by <c>MoveRateX=</c> / <c>MoveRateY=</c>.
///     Implicitly single-part (<c>Parts = 1</c>).
///   </item>
///   <item>
///     <b>New format</b> — bare numeric header:<br/>
///     <c>[350001]</c> with <c>Part=</c>, <c>Mesh0=</c>, <c>Texture0=</c>,
///     <c>Asb0=</c>, <c>Adb0=</c> per-slot keys (same layout as Armor.ini).
///   </item>
/// </list>
/// Both are parsed into the same <see cref="MountTypeInfo"/> model.
/// </summary>
public static class MountIniParser
{
    public static List<MountTypeInfo> Parse(string filePath)
    {
        var result = new List<MountTypeInfo>();
        if (!File.Exists(filePath)) return result;

        MountTypeInfo? current = null;

        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
                continue;

            // ── Section header ────────────────────────────────────────────
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Commit(result, current);
                current = null;

                var inner = line[1..^1].Trim();

                // Old format: [Mount410301]
                if (inner.StartsWith("Mount", StringComparison.OrdinalIgnoreCase))
                {
                    if (uint.TryParse(inner["Mount".Length..], out uint oldId))
                        current = new MountTypeInfo { Id = oldId, Parts = 1 };
                }
                // New format: [350001]
                else if (uint.TryParse(inner, out uint newId))
                {
                    current = new MountTypeInfo { Id = newId };
                }

                continue;
            }

            if (current == null) continue;

            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();

            // ── Old-format bare keys (single-part, slot 0) ────────────────
            //    Guard: key must NOT end with a digit (that would be a new-format Mesh0= etc.)
            if (key.Equals("Mesh", StringComparison.OrdinalIgnoreCase)
                && !char.IsDigit(key[^1]))
            {
                if (uint.TryParse(val, out uint v)) current.MeshIds[0] = v;
                continue;
            }
            if (key.Equals("Texture", StringComparison.OrdinalIgnoreCase)
                && !char.IsDigit(key[^1]))
            {
                if (uint.TryParse(val, out uint v)) current.TextureIds[0] = v;
                continue;
            }
            // Optional old-format extras (slot 0 only)
            if (key.Equals("Texture2", StringComparison.OrdinalIgnoreCase)
                && !char.IsDigit(key[^1]))
            {
                if (uint.TryParse(val, out uint v)) current.Texture2Ids[0] = v;
                continue;
            }
            if (key.Equals("MoveRateX", StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(val, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float v))
                    current.MoveRateX[0] = v;
                continue;
            }
            if (key.Equals("MoveRateY", StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(val, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float v))
                    current.MoveRateY[0] = v;
                continue;
            }

            // ── New-format scalar ─────────────────────────────────────────
            if (key == "Part")
            {
                if (int.TryParse(val, out int p))
                    current.Parts = Math.Clamp(p, 0, MountTypeInfo.MaxParts);
                continue;
            }

            // ── New-format per-slot: Mesh0, Texture0, Asb0, Adb0 ─────────
            if (key.StartsWith("Mesh", StringComparison.OrdinalIgnoreCase)
                && TrySlot(key, "Mesh", out int mi))
            { if (uint.TryParse(val, out uint v)) current.MeshIds[mi] = v; }
            else if (key.StartsWith("Texture", StringComparison.OrdinalIgnoreCase)
                     && TrySlot(key, "Texture", out int ti))
            { if (uint.TryParse(val, out uint v)) current.TextureIds[ti] = v; }
            else if (key.StartsWith("Asb", StringComparison.OrdinalIgnoreCase)
                     && TrySlot(key, "Asb", out int ai))
            { if (int.TryParse(val, out int v)) current.Asb[ai] = v; }
            else if (key.StartsWith("Adb", StringComparison.OrdinalIgnoreCase)
                     && TrySlot(key, "Adb", out int di))
            { if (int.TryParse(val, out int v)) current.Adb[di] = v; }
            // MixTex, MixOpt, Material -> ignored (same as Armor/Armet parsers)
        }

        Commit(result, current);
        return result;
    }

    private static void Commit(List<MountTypeInfo> list, MountTypeInfo? info)
    {
        if (info != null) list.Add(info);
    }

    private static bool TrySlot(string key, string prefix, out int index)
    {
        index = -1;
        var suffix = key[prefix.Length..];
        return suffix.Length > 0
            && int.TryParse(suffix, out index)
            && (uint)index < MountTypeInfo.MaxParts;
    }
}