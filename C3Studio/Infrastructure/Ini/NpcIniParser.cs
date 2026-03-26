using System;
using System.Collections.Generic;
using System.IO;
using C3Studio.Models;

namespace C3Studio.Infrastructure.Ini;

/// <summary>
/// Parses <c>ini/Npc.ini</c>. Mirrors the C++ <c>CreateNpcTypeInfo()</c> logic
/// but uses robust key=value scanning instead of strict <c>fscanf</c> ordering,
/// which allows optional/extra fields without breaking required ones.
/// </summary>
public static class NpcIniParser
{
    public static List<NpcTypeInfo> Parse(string filePath)
    {
        var result = new List<NpcTypeInfo>();
        if (!File.Exists(filePath)) return result;

        NpcTypeInfo? current = null;

        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.Trim();

            if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
                continue;

            // ── Section header [NpcTypeN] ─────────────────────────────
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (current != null && !string.IsNullOrEmpty(current.Name))
                    result.Add(current);

                var title = line[1..^1]; // strip [ ]
                if (title.StartsWith("NpcType", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(title["NpcType".Length..], out int type))
                {
                    current = new NpcTypeInfo { NpcType = type };
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

            switch (key)
            {
                case "Name":           current.Name            = val;                           break;
                case "SimpleObjID":    TrySetUInt(val, v => current.SimpleObjId    = v);       break;
                case "StandByMotion":  TrySetULong(val, v => current.StandByMotionId = v);     break;
                case "BlazeMotion":    TrySetULong(val, v => current.BlazeMotionId  = v);      break;
                case "BlazeMotion1":   TrySetULong(val, v => current.BlazeMotion1Id = v);      break;
                case "BlazeMotion2":   TrySetULong(val, v => current.BlazeMotion2Id = v);      break;
                case "RestMotion":     TrySetULong(val, v => current.RestMotionId   = v);      break;
                case "Effect":         current.Effect          = val;                           break;
                //case "ZoomPercent":    TrySetInt(val, v => current.ZoomPercent      = v);      break;
                //case "Note":           current.Note = val == "NULL" ? string.Empty : val;      break;
                //case "MouseSign":      TrySetInt(val, v => current.MouseSign        = v);      break;
                //case "ChangeDir":      TrySetInt(val, v => current.ChangeDir        = v != 0); break;
                //case "FrontBlock":     TrySetInt(val, v => current.FrontBlock       = v);      break;
                //case "BackBlock":      TrySetInt(val, v => current.BackBlock        = v);      break;
                //case "LeftBlock":      TrySetInt(val, v => current.LeftBlock        = v);      break;
                //case "RightBlock":     TrySetInt(val, v => current.RightBlock       = v);      break;
                case "ASB":            TrySetInt(val, v => current.Asb              = v);      break;
                case "ADB":            TrySetInt(val, v => current.Adb              = v);      break;
                case "FixDir":         TrySetInt(val, v => current.FixDir           = v);      break;
            }
        }

        if (current != null && !string.IsNullOrEmpty(current.Name))
            result.Add(current);

        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private static void TrySetUInt(string s, Action<uint> setter)
    {
        // SimpleObjID is stored zero-padded (e.g. "0211") → parse as decimal uint
        if (uint.TryParse(s, out var v)) setter(v);
    }
    private static void TrySetULong(string s, Action<ulong> setter)
    { if (ulong.TryParse(s, out var v)) setter(v); }
    private static void TrySetInt(string s, Action<int> setter)
    { if (int.TryParse(s, out var v)) setter(v); }
}
