namespace C3Studio.Core.Models;

/// <summary>A node in the asset tree (NPC, SimpleObj, category header…).</summary>
public class AssetNode
{
    public string Icon      { get; set; } = string.Empty;
    public string Label     { get; set; } = string.Empty;
    /// <summary>Asset path resolvable via IAssetFileService. Null for category headers.</summary>
    public AssetData? AssetData { get; set; }
    /// <summary>True when the node has at least one resolvable asset path.</summary>
    public bool IsLoadable => (AssetData?.MeshPaths.Length > 0 || AssetData?.Motions.Length > 0);

    public List<AssetNode> Children { get; } = new();
}
public sealed record MotionData(string Label, string Path);
public sealed class AssetData
{
    public string[] MeshPaths { get; init; } = [];
    public string[] TexturePaths { get; init; } = [];
    public MotionData[] Motions { get; init; } = [];
}
