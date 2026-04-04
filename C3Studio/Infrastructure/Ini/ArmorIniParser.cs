using C3Studio.Core.Models;
using System.IO;

namespace C3Studio.Infrastructure.Ini;

/// <summary>
/// Parses <c>ini/Armor.ini</c>. Two section-header formats coexist in the wild:
/// <list type="bullet">
///   <item>
///     <b>Old format</b> — used by <c>C3DRoleData::CreateRolePartInfo</c>:<br/>
///     <c>[Armor002000000]</c> with bare <c>Mesh=</c> / <c>Texture=</c> keys.
///     Implicitly single-part (<c>Parts = 1</c>).
///   </item>
///   <item>
///     <b>New format</b> — bare numeric header:<br/>
///     <c>[1000000]</c> with <c>Part=</c>, <c>Mesh0=</c>, <c>Texture0=</c>,
///     <c>Asb0=</c>, <c>Adb0=</c> per-slot keys.
///   </item>
/// </list>
/// Both are parsed into the same <see cref="ArmorTypeInfo"/> model.
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

            // -- Section header --------------------------------------------
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Commit(result, current);
                current = null;

                var inner = line[1..^1].Trim();

                // Old format: [Armor002000000]
                if (inner.StartsWith("Armor", StringComparison.OrdinalIgnoreCase))
                {
                    if (uint.TryParse(inner["Armor".Length..], out uint oldId))
                        current = new ArmorTypeInfo { Id = oldId, Parts = 1 };
                }
                // New format: [1000000]
                else if (uint.TryParse(inner, out uint newId))
                {
                    current = new ArmorTypeInfo { Id = newId };
                }

                continue;
            }

            if (current == null) continue;

            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();

            // -- Old-format bare keys (single-part) ------------------------
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

            // -- New-format scalar -----------------------------------------
            if (key == "Part")
            {
                if (int.TryParse(val, out int p))
                    current.Parts = Math.Clamp(p, 0, ArmorTypeInfo.MaxParts);
                continue;
            }

            // -- New-format per-slot: Mesh0, Texture0, Asb0, Adb0 ---------
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
            // MixTex, MixOpt, Material, Texture2, MoveRateX/Y -> ignored
        }

        Commit(result, current);
        return result;
    }

    private static void Commit(List<ArmorTypeInfo> list, ArmorTypeInfo? info)
    {
        if (info != null) list.Add(info);
    }

    private static bool TrySlot(string key, string prefix, out int index)
    {
        index = -1;
        var suffix = key[prefix.Length..];
        return suffix.Length > 0
            && int.TryParse(suffix, out index)
            && (uint)index < ArmorTypeInfo.MaxParts;
    }
}