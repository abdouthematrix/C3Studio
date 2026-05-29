using C3Studio.Core.Models;
using SharpDX.Mathematics.Interop;
using System.IO;

namespace C3Studio.Infrastructure.Ini;

/// <summary>
/// Parses <c>ini/3DSimpleObj.ini</c>. Mirrors C++ <c>Create3DSimpleObjInfo()</c>.
/// </summary>
public static class SimpleObjIniParser
{
    public static List<C3DSimpleObjInfo> Parse(string filePath)
    {
        if (!File.Exists(filePath)) return new();
        using var reader = new StreamReader(filePath);
        return Parse(reader);
    }
    public static List<C3DSimpleObjInfo> Parse(TextReader reader)
    {
        var result = new List<C3DSimpleObjInfo>();
        C3DSimpleObjInfo? current = null;
        int nextPartIdx = 0;

        string? rawLine;
        while ((rawLine = reader.ReadLine()) is not null)
        {
            var line = rawLine.Trim();

            if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
                continue;

            // ── Section header [ObjIDTypeN] ───────────────────────────
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (current != null) result.Add(current);

                var title = line[1..^1];
                if (title.StartsWith("ObjIDType", StringComparison.OrdinalIgnoreCase)
                    && uint.TryParse(title["ObjIDType".Length..], out uint typeId))
                {
                    current     = new C3DSimpleObjInfo { IdType = typeId };
                    nextPartIdx = 0;
                }
                else
                {
                    current = null;
                }
                continue;
            }

            if (current == null) continue;

            var eqIdx = line.IndexOf('=');
            if (eqIdx <= 0) continue;

            var key = line[..eqIdx].Trim();
            var val = line[(eqIdx + 1)..].Trim();

            if (key == "PartAmount")
            {
                if (int.TryParse(val, out int pa))
                    current.Parts = Math.Clamp(pa, 0, C3DSimpleObjInfo.MaxParts);
            }
            else if (key.StartsWith("Part") && !key.StartsWith("PartAmount"))
            {
                if (int.TryParse(key["Part".Length..], out int idx) && idx < C3DSimpleObjInfo.MaxParts)
                    if (uint.TryParse(val, out uint meshId))
                        current.MeshIds[idx] = meshId;
            }
            else if (key.StartsWith("Texture"))
            {
                if (int.TryParse(key["Texture".Length..], out int idx) && idx < C3DSimpleObjInfo.MaxParts)
                    if (uint.TryParse(val, out uint texId))
                        current.TextureIds[idx] = texId;
            }
        }

        if (current != null) result.Add(current);
        return result;
    }
}
