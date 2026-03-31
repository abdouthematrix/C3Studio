using C3Studio.Core.Models;
using System.IO;

namespace C3Studio.Infrastructure.Ini;

/// <summary>
/// Parses <c>ini/Armor.ini</c>.
/// <para>
/// Section headers are bare numeric IDs: <c>[1000000]</c>.
/// Per-slot keys are zero-indexed suffixes: <c>Mesh0</c>, <c>Texture0</c>,
/// <c>Asb0</c>, <c>Adb0</c>. Unknown keys (<c>MixTex0</c>, <c>MixOpt0</c>,
/// <c>Material0</c>) are silently skipped.
/// </para>
/// </summary>
public static class ArmorIniParser
{
    public static List<ArmorTypeInfo> Parse(string filePath)
    {
        var result = new List<ArmorTypeInfo>();
        if (!File.Exists(filePath)) return result;

        ArmorTypeInfo? current = null;

        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
                continue;

            // ── Section header [numericId] ─────────────────────────────────
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Commit(result, current);

                var inner = line[1..^1].Trim();
                if (uint.TryParse(inner, out uint id))
                    current = new ArmorTypeInfo { Id = id };
                else
                    current = null;

                continue;
            }

            if (current == null) continue;

            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();

            // ── Scalar ─────────────────────────────────────────────────────
            if (key == "Part")
            {
                if (int.TryParse(val, out int p))
                    current.Parts = Math.Clamp(p, 0, ArmorTypeInfo.MaxParts);
                continue;
            }

            // ── Per-slot: Mesh0, Texture0, Asb0, Adb0 ─────────────────────
            if (key.StartsWith("Mesh", StringComparison.OrdinalIgnoreCase)
                && TrySlot(key, "Mesh", out int mi))
            {
                if (uint.TryParse(val, out uint v)) current.MeshIds[mi] = v;
            }
            else if (key.StartsWith("Texture", StringComparison.OrdinalIgnoreCase)
                     && TrySlot(key, "Texture", out int ti))
            {
                if (uint.TryParse(val, out uint v)) current.TextureIds[ti] = v;
            }
            else if (key.StartsWith("Asb", StringComparison.OrdinalIgnoreCase)
                     && TrySlot(key, "Asb", out int ai))
            {
                if (int.TryParse(val, out int v)) current.Asb[ai] = v;
            }
            else if (key.StartsWith("Adb", StringComparison.OrdinalIgnoreCase)
                     && TrySlot(key, "Adb", out int di))
            {
                if (int.TryParse(val, out int v)) current.Adb[di] = v;
            }
            // MixTex, MixOpt, Material → ignored (rendering not affected)
        }

        Commit(result, current);
        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static void Commit(List<ArmorTypeInfo> list, ArmorTypeInfo? info)
    {
        if (info != null) list.Add(info);
    }

    private static bool TrySlot(string key, string prefix, out int index)
    {
        index = -1;
        var suffix = key[prefix.Length..];
        return int.TryParse(suffix, out index)
            && (uint)index < ArmorTypeInfo.MaxParts;
    }
}