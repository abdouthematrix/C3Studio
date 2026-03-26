namespace C3Studio.Core.Models;

/// <summary>A node in the asset tree (NPC, SimpleObj, category header…).</summary>
public class AssetNode
{
    public string Icon      { get; set; } = string.Empty;
    public string Label     { get; set; } = string.Empty;
    /// <summary>Asset path resolvable via IAssetFileService. Null for category headers.</summary>
    public string? AssetKey { get; set; }
    /// <summary>True when the node maps to a loadable .c3 file.</summary>
    public bool IsLoadable  => AssetKey != null;

    public List<AssetNode> Children { get; } = new();
}
