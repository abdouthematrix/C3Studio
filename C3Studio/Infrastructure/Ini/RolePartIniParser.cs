using C3Studio.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace C3Studio.Infrastructure.Ini;

/// <summary>
/// A single unified parser handling all role equipable parts: 
/// Armor, Armet, Weapon, Mount, Cape, Head, Misc, Pelvis, and Spirit.
/// Handles both legacy alpha-prefixed headers and modern bare numeric headers.
/// </summary>
public static class RolePartIniParser
{
    public static List<RolePart> Parse(string filePath, RolePartType partType)
    {
        if (!File.Exists(filePath)) return new();
        using var reader = new StreamReader(filePath);
        return Parse(reader, partType);
    }
    public static List<RolePart> Parse(TextReader reader, RolePartType partType)
    {
        var result = new List<RolePart>();

        string prefix = partType.ToString();
        RolePart? current = null;

        string? rawLine;
        while ((rawLine = reader.ReadLine()) is not null)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
                continue;

            // -- Section header parsing ------------------------------------
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Commit(result, current);
                current = null;

                var inner = line[1..^1].Trim();

                // Old legacy format: e.g. [Armor002000000]
                if (inner.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (uint.TryParse(inner[prefix.Length..], out uint oldId))
                        current = new RolePart { Id = oldId, PartType = partType, Parts = 1 };
                }
                // New format: e.g. [1000000]
                else if (uint.TryParse(inner, out uint newId))
                {
                    current = new RolePart { Id = newId, PartType = partType };
                }

                continue;
            }

            if (current == null) continue;

            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();

            // -- Old-format bare keys (implicitly single-part) -------------
            if (key.Equals("Mesh", StringComparison.OrdinalIgnoreCase) && !char.IsDigit(key[^1]))
            {
                if (uint.TryParse(val, out uint v)) current.MeshIds[0] = v;
                continue;
            }
            if (key.Equals("Texture", StringComparison.OrdinalIgnoreCase) && !char.IsDigit(key[^1]))
            {
                if (uint.TryParse(val, out uint v)) current.TextureIds[0] = v;
                continue;
            }

            // -- New-format part scalar ------------------------------------
            if (key.Equals("Part", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(val, out int p))
                    current.Parts = Math.Clamp(p, 0, RolePart.MaxParts);
                continue;
            }

            // -- New-format multi-slot keys: Mesh0, Texture0, Asb0, Adb0 ---
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
        }

        Commit(result, current);
        return result;
    }

    private static void Commit(List<RolePart> list, RolePart? info)
    {
        if (info != null) list.Add(info);
    }

    private static bool TrySlot(string key, string prefix, out int index)
    {
        index = -1;
        var suffix = key[prefix.Length..];
        return suffix.Length > 0
            && int.TryParse(suffix, out index)
            && (uint)index < RolePart.MaxParts;
    }
}