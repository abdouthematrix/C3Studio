using C3Studio.Core.Models;
using System.IO;
using System.Text.RegularExpressions;

namespace C3Studio.Infrastructure.Ini;

/// <summary>
/// Parses <c>ini/MagicEffect.ini</c> into a dictionary of <see cref="MagicSkillGroup"/>
/// entries keyed by their base skill ID (the section ID rounded down to the nearest 10).
///
/// File structure:
/// <code>
/// [100000]          ← base level (ID % 10 == 0)
/// Name=Thunder
/// Role3DEffectOfTarget=Thunder
/// IntoneEffect=Intone-1
/// Name1=ChasingFire                  ← skin-1 override for Name
/// Role3DEffectOfTarget1=AltThunder   ← skin-1 override for Role3DEffectOfTarget
///
/// [100001]          ← level variant — inherits base, stores only diffs
/// Desc=Upgrade~after~lvl~10
/// </code>
///
/// Key-routing rules applied in order for every ini key:
/// <list type="number">
///   <item><b>Exact property match</b> — if the full key (e.g. <c>WoundDurationOfTarget2</c>)
///         is a real <see cref="MagicEffect"/> property it is set directly.  This must
///         come first because some property names end in a digit and would otherwise be
///         misidentified as skin overrides.</item>
///   <item><b>Skin-suffix match</b> — if the key matches <c>PropName&lt;N&gt;</c> and
///         <c>PropName</c> is a real property, the value is stored as skin-N override.</item>
///   <item><b>Unknown key</b> — silently skipped (forward-compatibility).</item>
/// </list>
///
/// Inheritance: level variants (ID != baseGroupId) clone the base-level effect and
/// apply only the keys they declare, so <see cref="MagicEffect.Clone"/> always reflects
/// the fully-inherited state.
/// </summary>
public static class MagicEffectIniParser
{
    // Matches a trailing all-digit suffix: "Name1" → ("Name", 1), "Target2" → ("Target", 2)
    private static readonly Regex SkinSuffixRx =
        new(@"^([A-Za-z][A-Za-z0-9_]*)(\d+)$", RegexOptions.Compiled);

    public static Dictionary<int, MagicSkillGroup> Parse(string filePath)
    {
        var result = new Dictionary<int, MagicSkillGroup>();
        if (!File.Exists(filePath)) return result;

        // ── Pass 1: slurp all sections into a raw key→value map ──────────
        var sections = new Dictionary<int, Dictionary<string, string>>();

        int? currentId = null;
        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith(';') || line.StartsWith("//"))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (int.TryParse(line[1..^1].Trim(), out int id))
                {
                    currentId = id;
                    sections[id] = new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    currentId = null; // non-numeric section — skip
                }
                continue;
            }

            if (currentId is null) continue;

            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim().Replace("~", " ");
            sections[currentId.Value][key] = value;
        }

        // ── Pass 2: build skill groups in section order ───────────────────
        foreach (var (id, kvp) in sections.OrderBy(s => s.Key))
        {
            int baseGroupId = (id / 10) * 10;
            bool isBaseLevel = id == baseGroupId;

            if (!result.TryGetValue(baseGroupId, out var group))
            {
                group = new MagicSkillGroup();
                result[baseGroupId] = group;
            }

            // Base level → fresh effect.  Variant level → clone of base (inheritance).
            MagicEffect current = isBaseLevel
                ? new MagicEffect()
                : (group.Levels.TryGetValue(baseGroupId, out var baseEffect)
                       ? baseEffect.Clone()
                       : new MagicEffect());

            foreach (var (rawKey, rawValue) in kvp)
            {
                // ── Rule 1: exact property match wins unconditionally ─────
                // Checked before the skin-suffix path so that properties whose
                // names end in a digit (e.g. WoundDurationOfTarget2) are never
                // misrouted to a skin bucket.
                if (MagicEffectReflection.HasProperty(rawKey))
                {
                    MagicEffectReflection.TrySet(current, rawKey, rawValue);
                    continue;
                }

                // ── Rule 2: skin-suffix (e.g. Name1, Role3DEffectOfTarget2) ──
                var m = SkinSuffixRx.Match(rawKey);
                if (!m.Success) continue; // Rule 3: unknown key — skip

                string propName = m.Groups[1].Value;
                int skinId = int.Parse(m.Groups[2].Value);

                if (!MagicEffectReflection.HasProperty(propName)) continue;

                if (!group.Skins.ContainsKey(skinId))
                    group.Skins[skinId] = new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);

                group.Skins[skinId][propName] = rawValue;
            }

            group.Levels[id] = current;
        }

        return result;
    }
}

/// <summary>
/// Cached reflection helpers for <see cref="MagicEffect"/>.
/// Pays the <c>GetProperty</c> cost once per unique name across the whole file.
/// </summary>
internal static class MagicEffectReflection
{
    private static readonly Dictionary<string, System.Reflection.PropertyInfo?> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    private static System.Reflection.PropertyInfo? Lookup(string name)
    {
        if (!_cache.TryGetValue(name, out var pi))
        {
            pi = typeof(MagicEffect).GetProperty(name,
                     System.Reflection.BindingFlags.Public |
                     System.Reflection.BindingFlags.Instance |
                     System.Reflection.BindingFlags.IgnoreCase);
            _cache[name] = pi;
        }
        return pi;
    }

    public static bool HasProperty(string name) => Lookup(name) is not null;

    public static void TrySet(MagicEffect target, string name, string rawValue)
    {
        var pi = Lookup(name);
        if (pi is null || !pi.CanWrite) return;

        object value = pi.PropertyType switch
        {
            var t when t == typeof(bool) => rawValue == "1" ||
                                             rawValue.Equals("true",
                                                 StringComparison.OrdinalIgnoreCase),
            var t when t == typeof(int) => int.TryParse(rawValue, out int i) ? i : 0,
            var t when t == typeof(float) => float.TryParse(rawValue,
                                                System.Globalization.NumberStyles.Float,
                                                System.Globalization.CultureInfo.InvariantCulture,
                                                out float f) ? f : 0f,
            _ => rawValue,
        };

        pi.SetValue(target, value);
    }
}