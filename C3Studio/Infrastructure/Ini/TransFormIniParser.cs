using C3Studio.Core.Models;
using System.IO;

namespace C3Studio.Infrastructure.Ini;

/// <summary>
/// Parses <c>ini/TransForm.ini</c> into a list of <see cref="TransformInfo"/> entries.
///
/// File structure – each section header IS the transform index:
/// <code>
/// [980]
/// Jump=0
/// AddSize=0
/// Scale=100
/// Look=98
/// Armet=0
/// RWeapon=0
/// LWeapon=0
/// Misc=0
/// Mount=0
/// </code>
///
/// Differences from <c>AdditiveSize.ini</c>:
/// <list type="bullet">
///   <item>No <c>[Header]</c> section — section name is the numeric index directly.</item>
///   <item><c>Jump</c> is an integer (0/1) instead of the "ON"/"OFF" string.</item>
///   <item><c>AddSize</c> key (not <c>AdditiveSize</c>).</item>
///   <item>Additional equipment-slot fields: Armet, RWeapon, LWeapon, Misc, Mount.</item>
/// </list>
/// </summary>
public static class TransFormIniParser
{
    public static List<TransformInfo> Parse(string filePath)
    {
        var result = new List<TransformInfo>();
        if (!File.Exists(filePath)) return result;

        // ── Slurp all sections into a raw key→value map ──────────────────
        var sections = new Dictionary<int, Dictionary<string, string>>();

        int? currentIndex = null;
        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("//")) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var name = line[1..^1].Trim();
                if (int.TryParse(name, out int idx))
                {
                    currentIndex = idx;
                    if (!sections.ContainsKey(idx))
                        sections[idx] = new Dictionary<string, string>(
                            StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    currentIndex = null; // non-numeric section — skip
                }
                continue;
            }

            if (currentIndex == null) continue;

            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            sections[currentIndex.Value][line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }

        // ── Build TransformInfo list in ascending index order ─────────────
        foreach (var (index, sec) in sections.OrderBy(kv => kv.Key))
        {
            var info = new TransformInfo { Index = index };

            if (sec.TryGetValue("AddSize", out var addStr)
                && int.TryParse(addStr, out int addSize))
                info.AdditiveSize = addSize;

            if (sec.TryGetValue("Look", out var lookStr)
                && int.TryParse(lookStr, out int look))
                info.Look = look;

            if (sec.TryGetValue("Scale", out var scaleStr)
                && int.TryParse(scaleStr, out int scale))
                info.Scale = scale;

            // Jump: integer (0 = false, non-zero = true)
            if (sec.TryGetValue("Jump", out var jumpStr)
                && int.TryParse(jumpStr, out int jump))
                info.CanJump = jump != 0;

            if (sec.TryGetValue("Armet", out var armetStr)
                && int.TryParse(armetStr, out int armet))
                info.Armet = armet;

            if (sec.TryGetValue("RWeapon", out var rStr)
                && int.TryParse(rStr, out int rw))
                info.RWeapon = rw;

            if (sec.TryGetValue("LWeapon", out var lStr)
                && int.TryParse(lStr, out int lw))
                info.LWeapon = lw;

            if (sec.TryGetValue("Misc", out var miscStr)
                && int.TryParse(miscStr, out int misc))
                info.Misc = misc;

            if (sec.TryGetValue("Mount", out var mountStr)
                && int.TryParse(mountStr, out int mount))
                info.Mount = mount;

            result.Add(info);
        }

        return result;
    }
}