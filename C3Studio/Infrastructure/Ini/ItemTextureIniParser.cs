using C3Studio.Core.Models;
using System.IO;

namespace C3Studio.Infrastructure.Ini;

/// <summary>
/// Parses <c>ini/ItemTexture.ini</c>.
/// Each section declares a base item ID and up to <c>Amount</c> color-variant
/// texture mappings.
/// <code>
/// [900000]
/// Amount=7
/// Color0=3
/// Texture0=900300
/// Color1=4
/// Texture1=900400
/// ...
/// </code>
/// </summary>
public static class ItemTextureIniParser
{
    public static List<ItemTextureInfo> Parse(string filePath)
    {
        var result = new List<ItemTextureInfo>();
        if (!File.Exists(filePath)) return result;

        ItemTextureInfo? current = null;

        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
                continue;

            // ── Section header [900000] ───────────────────────────────────
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Commit(result, current);
                current = null;

                var inner = line[1..^1].Trim();
                if (uint.TryParse(inner, out uint id))
                    current = new ItemTextureInfo { Id = id };

                continue;
            }

            if (current == null) continue;

            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();

            // ── Scalar ────────────────────────────────────────────────────
            if (key.Equals("Amount", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(val, out int a))
                    current.Amount = Math.Clamp(a, 0, ItemTextureInfo.MaxColors);
                continue;
            }

            // ── Per-slot: Color0, Texture0 … ──────────────────────────────
            if (key.StartsWith("Color", StringComparison.OrdinalIgnoreCase)
                && TrySlot(key, "Color", out int ci))
            {
                if (byte.TryParse(val, out byte v)) current.Colors[ci] = v;
            }
            else if (key.StartsWith("Texture", StringComparison.OrdinalIgnoreCase)
                     && TrySlot(key, "Texture", out int ti))
            {
                if (uint.TryParse(val, out uint v))                     
                    current.TextureIds[ti] = v;
            }
            // Unknown keys → ignored
        }

        Commit(result, current);
        return result;
    }

    private static void Commit(List<ItemTextureInfo> list, ItemTextureInfo? info)
    {
        if (info != null) list.Add(info);
    }

    private static bool TrySlot(string key, string prefix, out int index)
    {
        index = -1;
        var suffix = key[prefix.Length..];
        return suffix.Length > 0
            && int.TryParse(suffix, out index)
            && (uint)index < ItemTextureInfo.MaxColors;
    }
}