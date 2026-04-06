using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using C3Studio.Core.Services;
using C3Studio.Infrastructure.C3Format;

namespace C3Studio.Infrastructure.Loading;

/// <summary>
/// Single source of truth for loading C3 assets (models, textures, motions).
/// Handles the full resolution chain: cache → AssetService stream → filesystem path.
/// Callers describe *what* they want; this class decides *how* to find it.
/// </summary>
public sealed class C3AssetLoader
{
    private readonly GraphicsDevice _gd;
    private IAssetFileService? _assets;

    private static readonly string[] TextureExtensions = { ".dds", ".tga", ".png", ".jpg" };

    public C3AssetLoader(GraphicsDevice gd, IAssetFileService? assets = null)
    {
        _gd = gd;
        _assets = assets;
    }

    public void SetAssetService(IAssetFileService assets) => _assets = assets;

    // ── Model ─────────────────────────────────────────────────────────────

    /// <summary>Loads a single model. Returns null and logs on failure.</summary>
    public C3Model? LoadModel(string relativePath)
    {
        try
        {
            if (_assets != null)
            {
                using var stream = _assets.Open(relativePath);
                return C3Model.LoadFromStream(stream, _gd);
            }
            return C3Model.Load(relativePath, _gd);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[C3AssetLoader] LoadModel '{relativePath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>Loads a model from an already-open stream.</summary>
    public C3Model LoadModel(Stream stream) =>
        C3Model.LoadFromStream(stream, _gd);

    /// <summary>
    /// Loads multiple (mesh, texture, asb, adb) tuples and merges them into one C3Model.
    /// The first valid part becomes the base; remaining parts are appended.
    /// D3D blend factors from each tuple are stamped onto all PHYs belonging to that part.
    /// Returns (null, 0) if no parts could be loaded.
    /// <para>
    /// The returned <c>PartCount</c> must be passed to
    /// <see cref="ApplyMotion(C3Model, string, Matrix, int)"/> so motions are
    /// replicated correctly — one copy per part per PHY.
    /// </para>
    /// </summary>
    public (C3Model? Model, int PartCount) LoadAndMerge(
        IEnumerable<(string MeshPath, string? TexturePath, int Asb, int Adb)> parts)
    {
        C3Model? merged = null;
        int partCount = 0;

        foreach (var (meshPath, texturePath, asb, adb) in parts)
        {
            var part = LoadModel(meshPath);
            if (part == null) continue;

            // Default D3D blend values: 5 = SrcAlpha, 6 = InvSrcAlpha
            int effectiveAsb = asb > 0 ? asb : 5;
            int effectiveAdb = adb > 0 ? adb : 6;

            if (!string.IsNullOrEmpty(texturePath))
            {
                int texIdx = LoadTexture(texturePath);
                if (texIdx >= 0)
                {
                    foreach (var phy in part.Phys)
                        phy.TexIndex = texIdx;
                    foreach (var phy in part.Shapes)
                        phy.TexIndex = texIdx;
                    foreach (var phy in part.Ptcls)
                        phy.TexIndex = texIdx;
                    foreach (var phy in part.Scenes)
                        phy.TexIndex = texIdx;
                }
            }

            // Stamp the part's blend factors onto every PHY it owns.
            foreach (var phy in part.Phys)
            {
                phy.BlendAsb = effectiveAsb;
                phy.BlendAdb = effectiveAdb;
                phy.PartIndex = partCount;
            }
            foreach (var phy in part.Shapes)
            {
                phy.BlendAsb = effectiveAsb;
                phy.BlendAdb = effectiveAdb;
                phy.PartIndex = partCount;
            }
            foreach (var phy in part.Ptcls)
            {
                phy.BlendAsb = effectiveAsb;
                phy.BlendAdb = effectiveAdb;
                phy.PartIndex = partCount;
            }
            foreach (var phy in part.Scenes)
            {
                phy.BlendAsb = effectiveAsb;
                phy.BlendAdb = effectiveAdb;
                phy.PartIndex = partCount;
            }
            foreach (var mot in part.Motions)
                mot.PartIndex = partCount;
            if (merged == null)
            {
                merged = part;
            }
            else
            {
                foreach (var phy in part.Phys) merged.Phys.Add(phy);
                foreach (var phy in part.Shapes) merged.Shapes.Add(phy);
                foreach (var phy in part.Ptcls) merged.Ptcls.Add(phy);
                foreach (var phy in part.Scenes) merged.Scenes.Add(phy);
                foreach (var mot in part.Motions) merged.Motions.Add(mot);
            }

            partCount++;
        }

        return (merged, partCount);
    }

    /// <summary>
    /// Convenience overload for callers that have no per-part blend info.
    /// All parts receive the default blend (5/6 = standard alpha blend).
    /// </summary>
    public (C3Model? Model, int PartCount) LoadAndMerge(
        IEnumerable<(string MeshPath, string? TexturePath)> parts) =>
        LoadAndMerge(parts.Select(p => (p.MeshPath, p.TexturePath, 5, 6)));

    // ── Texture ───────────────────────────────────────────────────────────

    /// <summary>
    /// Loads a texture with a full fallback chain:
    /// 1. Already in the C3Texture cache → return existing slot.
    /// 2. Open via AssetService (WDF-aware).
    /// 3. Load directly from the filesystem.
    /// Returns -1 on failure.
    /// </summary>
    public int LoadTexture(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return -1;

        // 1 – cache hit
        int cached = C3Texture.FindByName(relativePath);
        if (cached >= 0) return cached;

        try
        {
            // 2 – AssetService (handles WDF archives)
            if (_assets != null)
            {
                using var stream = _assets.Open(relativePath);
                var tex = DecodeTexture(stream, relativePath);
                return C3Texture.InsertTexture(relativePath, tex);
            }

            // 3 – direct filesystem
            return C3Texture.Texture_Load(relativePath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[C3AssetLoader] LoadTexture '{relativePath}': {ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// Resolves the best texture for a model using the following priority:
    /// 1. <paramref name="explicitTexturePath"/> (if provided).
    /// 2. Same directory as the model, same base name, first matching extension.
    /// Returns -1 if nothing found.
    /// </summary>
    public int ResolveTextureForModel(string modelPath, string? explicitTexturePath = null)
    {
        if (!string.IsNullOrEmpty(explicitTexturePath))
            return LoadTexture(explicitTexturePath);

        var dir = Path.GetDirectoryName(modelPath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(modelPath);

        foreach (var ext in TextureExtensions)
        {
            var candidate = Path.Combine(dir, baseName + ext)
                               .Replace('\\', '/');
            int idx = LoadTexture(candidate);
            if (idx >= 0) return idx;
        }

        return -1;
    }

    // ── Motion ────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies a motion file to <paramref name="model"/>.
    /// <para>
    /// For merged models pass <paramref name="partCount"/> &gt; 1: the source
    /// motions are replicated once per part so every PHY gets a bound motion
    /// after <c>BindPhyMotions</c> runs.
    /// </para>
    /// Uses AssetService when available, falling back to a direct file path.
    /// Swallows and logs exceptions so a missing motion never crashes the caller.
    /// </summary>
    public void ApplyMotion(C3Model model, string relativePath, Matrix rotation,
                            int partCount = 1)
    {
        if (string.IsNullOrEmpty(relativePath)) return;
        try
        {
            C3Model? motionModel = LoadModel(relativePath);
            if (motionModel == null) return;

            ReplicateMotions(model, motionModel, partCount, rotation);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[C3AssetLoader] ApplyMotion '{relativePath}': {ex.Message}");
        }
    }

    /// <summary>Applies a motion from an already-open stream.</summary>
    public void ApplyMotion(C3Model model, Stream stream, Matrix rotation,
                            int partCount = 1)
    {
        try
        {
            var motionModel = C3Model.LoadFromStream(stream);
            ReplicateMotions(model, motionModel, partCount, rotation);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[C3AssetLoader] ApplyMotion (stream): {ex.Message}");
        }
    }

    /// <summary>
    /// Clears the target model's motions and fills them by repeating the
    /// source motions <paramref name="partCount"/> times — one full copy per
    /// merged part — then re-binds all PHYs.
    /// </summary>
    private static void ReplicateMotions(C3Model target, C3Model source,
                                         int partCount, Matrix rotation)
    {
        target.Motions.Clear();
        for (int i = 0; i < partCount; i++)
            foreach (var m in source.Motions)
                target.Motions.Add(m);

        C3Model.BindPhyMotions(target, rotation);
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private Texture2D DecodeTexture(Stream stream, string nameHint)
    {
        var ext = Path.GetExtension(nameHint).ToLowerInvariant();
        using var br = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        return ext switch
        {
            ".dds" => DDSLoader.Load(_gd, br),
            ".tga" => TGALoader.Load(_gd, br),
            _ => Texture2D.FromStream(_gd, stream)
        };
    }
}