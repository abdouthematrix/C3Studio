using C3Studio.Core.Models;
using System.IO;

namespace C3Studio.Infrastructure.Ini;

/// <summary>
/// Parses <c>ini/AdditiveSize.ini</c> into a list of <see cref="TransformInfo"/> entries,
/// replicating <c>C3DRoleData::CreateTransFormInfo</c>.
///
/// File structure:
/// <code>
/// [Header]
/// Amount=44
/// Index0=0
/// Index1=98
/// …
///
/// [Transform0]
/// AdditiveSize=0
/// Jump=ON
/// Look=0
/// Scale=100
/// …
/// </code>
/// The <c>[Header]</c> section drives the read order: only indices listed there are
/// loaded, and they are returned in the same order as the original C++ push_back loop.
/// </summary>
public static class AdditiveIniParser
{
    public static List<TransformInfo> Parse(string filePath)
    {
        var result = new List<TransformInfo>();
        if (!File.Exists(filePath)) return result;

        // ── Pass 1: slurp all sections into a raw key→value map ──────────
        var sections = new Dictionary<string, Dictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase);

        string? currentSection = null;
        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("//")) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                if (!sections.ContainsKey(currentSection))
                    sections[currentSection] = new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);
                continue;
            }

            if (currentSection == null) continue;

            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            sections[currentSection][line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }

        // ── Pass 2: read header ───────────────────────────────────────────
        if (!sections.TryGetValue("Header", out var header)) return result;

        if (!header.TryGetValue("Amount", out var amtStr)
            || !int.TryParse(amtStr, out int amount)
            || amount <= 0)
            return result;

        // ── Pass 3: iterate indices in header order (mirrors C++ loop) ────
        for (int i = 0; i < amount; i++)
        {
            if (!header.TryGetValue($"Index{i}", out var idxStr)
                || !int.TryParse(idxStr, out int index))
                continue;

            string sectionName = $"Transform{index}";
            if (!sections.TryGetValue(sectionName, out var sec)) continue;

            var info = new TransformInfo { Index = index };

            if (sec.TryGetValue("AdditiveSize", out var addStr)
                && int.TryParse(addStr, out int addSize))
                info.AdditiveSize = addSize;

            if (sec.TryGetValue("Look", out var lookStr)
                && int.TryParse(lookStr, out int look))
                info.Look = look;

            if (sec.TryGetValue("Scale", out var scaleStr)
                && int.TryParse(scaleStr, out int scale))
                info.Scale = scale;

            if (sec.TryGetValue("Jump", out var jump))
                info.CanJump = string.Equals(jump, "ON", StringComparison.OrdinalIgnoreCase);

            result.Add(info);
        }

        return result;
    }
}