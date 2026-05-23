namespace C3Studio.Core.Models;

public sealed class TmeEntry
{
    public string EffectKey { get; init; } = string.Empty;
    public uint Delay { get; init; }
    public uint RandomDelay { get; init; }
    public int OffsetX { get; init; }
    public int OffsetY { get; init; }
}