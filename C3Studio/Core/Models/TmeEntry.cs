namespace C3Studio.Core.Models;

/// <summary>One timed-effect entry inside a .tme file.</summary>
public sealed class TmeEntry
{
    /// <summary>Effect key that maps to a C3DEffect (e.g. "flysword").</summary>
    public string EffectKey { get; init; } = string.Empty;

    /// <summary>Delay before the effect starts, in milliseconds.</summary>
    public uint Delay { get; init; }

    /// <summary>Reserved / always 0.</summary>
    public uint Reserved { get; init; }

    /// <summary>How long the effect lives in milliseconds. -2 = infinite.</summary>
    public int Duration { get; init; }

    /// <summary>Repeat interval in milliseconds. -2 = no repeat.</summary>
    public int Interval { get; init; }
}