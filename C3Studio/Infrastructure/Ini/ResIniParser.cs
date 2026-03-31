using System.IO;

namespace C3Studio.Infrastructure.Ini;

/// <summary>
/// Parses the shared <c>id=filepath</c> format used by 3dobj.ini, 3dtexture.ini,
/// 3dmotion.ini, WeaponMotion.ini, MountMotion.ini, etc.
///
/// Keys are <see cref="ulong"/> — motion IDs such as 9990010100 exceed uint32 max.
/// </summary>
public static class ResIniParser
{
    public static Dictionary<ulong, string> Parse(string filePath)
    {
        var map = new Dictionary<ulong, string>();
        if (!File.Exists(filePath)) return map;

        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("//")) continue;

            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            if (ulong.TryParse(line[..eq].Trim(), out ulong id))
            {
                var path = line[(eq + 1)..].Trim();
                if (!string.IsNullOrEmpty(path))
                    map[id] = path;
            }
        }
        return map;
    }
}
