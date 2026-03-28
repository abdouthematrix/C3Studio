using System;
using System.Collections.Generic;
using System.IO;
using C3Studio.Models;

namespace C3Studio.Infrastructure.Ini;

/// <summary>
/// Parses <c>ini/3DEffect.ini</c>. Mirrors C++ <c>CreateMy3DEffectInfo()</c>
/// but uses robust key=value scanning instead of strict <c>fscanf</c> ordering,
/// so optional fields (OffsetZ, ShapeAir, ColorEnable) never break required ones.
/// </summary>
/// <remarks>
/// Section headers are bare numbers: <c>[10000]</c>.
/// Per-slot keys are zero-indexed suffixes: <c>EffectId0</c>, <c>TextureId0</c>,
/// <c>ASB0</c>, <c>ADB0</c>, …
/// </remarks>
public static class EffectIniParser
{
    public static List<C3DEffectInfo> Parse(string filePath)
    {
        var result = new List<C3DEffectInfo>();
        if (!File.Exists(filePath)) return result;

        C3DEffectInfo? current = null;

        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
                continue;

            // ── Section header [N] ────────────────────────────────────────
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Commit(result, current);

                var inner = line[1..^1].Trim();
                if (!string.IsNullOrEmpty(inner))
                {
                    current = new C3DEffectInfo { Key = inner };
                    if (uint.TryParse(inner, out uint numericId))
                        current.Id = numericId;
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

            // ── Scalar keys ───────────────────────────────────────────────
            switch (key)
            {
                case "Amount":
                    TrySetInt(val, v =>
                        current.Amount = Math.Clamp(v, 0, C3DEffectInfo.MaxEffects));
                    continue;
                case "Delay": TrySetInt(val, v => current.Delay = v); continue;
                case "LoopTime": TrySetInt(val, v => current.LoopTime = v); continue;
                case "FrameInterval": TrySetInt(val, v => current.FrameInterval = v); continue;
                case "LoopInterval": TrySetInt(val, v => current.LoopInterval = v); continue;
                case "OffsetX": TrySetInt(val, v => current.OffsetX = v); continue;
                case "OffsetY": TrySetInt(val, v => current.OffsetY = v); continue;
                case "OffsetZ": TrySetInt(val, v => current.OffsetZ = v); continue;
                case "ShapeAir": TrySetInt(val, v => current.ShapeAir = v); continue;
                case "ColorEnable": TrySetInt(val, v => current.ColorEnable = v); continue;
                case "Lev": TrySetInt(val, v => current.Lev = v); continue;
            }

            // ── Per-slot keys  EffectId0 / TextureId0 / ASB0 / ADB0 ──────
            if (key.StartsWith("EffectId", StringComparison.OrdinalIgnoreCase))
            {
                if (TrySlotIndex(key, "EffectId", out int i))
                    TrySetUInt(val, v => current.EffectIds[i] = v);
            }
            else if (key.StartsWith("TextureId", StringComparison.OrdinalIgnoreCase))
            {
                if (TrySlotIndex(key, "TextureId", out int i))
                    TrySetUInt(val, v => current.TextureIds[i] = v);
            }
            else if (key.StartsWith("ASB", StringComparison.OrdinalIgnoreCase))
            {
                if (TrySlotIndex(key, "ASB", out int i))
                    TrySetInt(val, v => current.Asb[i] = v);
            }
            else if (key.StartsWith("ADB", StringComparison.OrdinalIgnoreCase))
            {
                if (TrySlotIndex(key, "ADB", out int i))
                    TrySetInt(val, v => current.Adb[i] = v);
            }
        }

        Commit(result, current);
        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static void Commit(List<C3DEffectInfo> list, C3DEffectInfo? info)
    {
        if (info != null) list.Add(info);
    }

    /// <summary>
    /// Extracts the zero-based slot index from a suffixed key such as "EffectId2".
    /// Returns false if the suffix is non-numeric or out of range.
    /// </summary>
    private static bool TrySlotIndex(string key, string prefix, out int index)
    {
        index = -1;
        var suffix = key[prefix.Length..];
        return int.TryParse(suffix, out index)
            && index >= 0
            && index < C3DEffectInfo.MaxEffects;
    }

    private static void TrySetInt(string s, Action<int> setter)
    { if (int.TryParse(s, out var v)) setter(v); }

    private static void TrySetUInt(string s, Action<uint> setter)
    { if (uint.TryParse(s, out var v)) setter(v); }
}