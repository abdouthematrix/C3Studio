namespace C3Studio.Core.Models;

/// <summary>A node in the asset tree (NPC, SimpleObj, category header…).</summary>
public class AssetNode
{
    public string Icon { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
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

    /// <summary>
    /// Per-part D3D source-blend factor (index matches <see cref="MeshPaths"/>).
    /// 0 or out-of-range → default 5 (D3DBLEND_SRCALPHA).
    /// </summary>
    public int[] Asb { get; init; } = [];

    /// <summary>
    /// Per-part D3D destination-blend factor (index matches <see cref="MeshPaths"/>).
    /// 0 or out-of-range → default 6 (D3DBLEND_INVSRCALPHA).
    /// </summary>
    public int[] Adb { get; init; } = [];

    /// <summary>Returns the effective Asb for part <paramref name="i"/>, falling back to 5.</summary>
    public int GetAsb(int i) => (i < Asb.Length && Asb[i] > 0) ? Asb[i] : 5;

    /// <summary>Returns the effective Adb for part <paramref name="i"/>, falling back to 6.</summary>
    public int GetAdb(int i) => (i < Adb.Length && Adb[i] > 0) ? Adb[i] : 6;
}