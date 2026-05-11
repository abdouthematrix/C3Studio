using C3Studio.Core.Models;
using System.IO;

namespace C3Studio.Infrastructure.Ini;

/// <summary>
/// Parses <c>ini/3DSimpleRole.ini</c> into a list of <see cref="SimpleRoleTypeInfo"/>.
///
/// Sections are named <c>[RoleN]</c> where N is a non-negative integer.
/// Key names are case-insensitive.
/// </summary>
public static class SimpleRoleIniParser
{
    public static List<SimpleRoleTypeInfo> Parse(string path)
    {
        var results = new List<SimpleRoleTypeInfo>();
        if (!File.Exists(path)) return results;

        SimpleRoleTypeInfo? current = null;

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == ';') continue;

            // ── Section header ────────────────────────────────────────────
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (current != null)
                    results.Add(current);

                var header = line[1..^1].Trim(); // e.g. "Role0"
                if (!header.StartsWith("Role", StringComparison.OrdinalIgnoreCase))
                    continue;

                var suffix = header["Role".Length..];
                if (!int.TryParse(suffix, out int index))
                    continue;

                current = new SimpleRoleTypeInfo { Key = header, Index = index };
                continue;
            }

            if (current == null) continue;

            // ── Key = Value ───────────────────────────────────────────────
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();

            if (key.Equals("3DSimpleObjID", StringComparison.OrdinalIgnoreCase)
                && uint.TryParse(val, out uint objId))
                current.SimpleObjId = objId;

            else if (key.Equals("3DStandByMotion", StringComparison.OrdinalIgnoreCase)
                     && ulong.TryParse(val, out ulong standBy))
                current.StandByMotionId = standBy;

            else if (key.Equals("3DBlazeMotion", StringComparison.OrdinalIgnoreCase)
                     && ulong.TryParse(val, out ulong blaze))
                current.BlazeMotionId = blaze;

            else if (key.Equals("BlazeSound", StringComparison.OrdinalIgnoreCase))
                current.BlazeSound = val;

            else if (key.Equals("Look", StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(val, out int look))
                current.Look = look;

            else if (key.Equals("Armor", StringComparison.OrdinalIgnoreCase)
                     && uint.TryParse(val, out uint armor))
                current.RawArmorId = armor;

            else if (key.Equals("Hair", StringComparison.OrdinalIgnoreCase)
                     && uint.TryParse(val, out uint hair))
                current.RawHairId = hair;

            else if (key.Equals("F3DEffect", StringComparison.OrdinalIgnoreCase))
                current.FEffect = val;

            else if (key.Equals("B3DEffect", StringComparison.OrdinalIgnoreCase))
                current.BEffect = val;
        }

        if (current != null)
            results.Add(current);

        return results;
    }
}
