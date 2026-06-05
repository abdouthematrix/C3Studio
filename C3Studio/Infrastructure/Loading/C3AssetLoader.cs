using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using C3Studio.Core.Services;
using C3Studio.Infrastructure.C3Format;
using C3Studio.Infrastructure.Rendering;
using C3Studio.Core.Models;

namespace C3Studio.Infrastructure.Loading;

public sealed class C3AssetLoader
{
    private readonly GraphicsDevice _gd;
    private IAssetFileService? _assets;

    public C3AssetLoader(GraphicsDevice gd, IAssetFileService? assets = null)
    {
        _gd = gd;
        _assets = assets;
    }

    public void SetAssetService(IAssetFileService assets) => _assets = assets;

    // ── Model ─────────────────────────────────────────────────────────────
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

    // ── New per-part API ──────────────────────────────────────────────────
    public C3RolePart? LoadPart(
        string meshPath,
        string? texturePath,
        string slotName,
        uint rolePartId,
        int asb = 5,
        int adb = 6)
    {
        var model = LoadModel(meshPath);
        if (model == null) return null;

        int effectiveAsb = asb > 0 ? asb : 5;
        int effectiveAdb = adb > 0 ? adb : 6;

        ApplyBlend(model, effectiveAsb, effectiveAdb);

        if (!string.IsNullOrEmpty(texturePath))
            BindTextureToPart(model, texturePath);

        return new C3RolePart(model, slotName, rolePartId, effectiveAsb, effectiveAdb);
    }

    // ── Effect loading ────────────────────────────────────────────────────

    /// <summary>
    /// Loads a <see cref="C3Effect"/> from a <b>single</b> mesh/texture pair.
    /// Convenience wrapper around the multi-descriptor overload.
    /// GPU initialisation is the caller's responsibility.
    /// </summary>
    public C3Effect? LoadEffect(
        string meshPath,
        string? texturePath,
        string slotName = "Effect",
        int asb = 5,
        int adb = 6)
    {
        var desc = new EffectDescriptor(meshPath, texturePath, asb, adb);
        return LoadEffect([desc], slotName);
    }

    /// <summary>
    /// Loads a <see cref="C3Effect"/> from <b>multiple</b> <see cref="EffectDescriptor"/> slots
    /// (e.g. an ini effect entry with <c>Amount &gt; 1</c>).
    /// Each descriptor becomes one <see cref="C3Model"/> inside the effect.
    /// GPU initialisation is the caller's responsibility.
    /// </summary>
    public C3Effect? LoadEffect(
        IEnumerable<EffectDescriptor> descriptors,
        string slotName = "Effect")
    {
        var models = new List<C3Model>();
        int effectiveAsb = 5, effectiveAdb = 6;

        foreach (var desc in descriptors)
        {
            if (string.IsNullOrEmpty(desc.MeshPath) || desc.MeshPath.StartsWith('?'))
                continue;

            var model = LoadModel(desc.MeshPath);
            if (model == null) continue;

            effectiveAsb = desc.Asb > 0 ? desc.Asb : 5;
            effectiveAdb = desc.Adb > 0 ? desc.Adb : 6;

            ApplyBlend(model, effectiveAsb, effectiveAdb);

            if (!string.IsNullOrEmpty(desc.TexturePath))
                BindTextureToPart(model, desc.TexturePath);

            models.Add(model);
        }

        if (models.Count == 0) return null;

        // Use the blend values from the last resolved descriptor
        // (all slots in a single effect entry share the same visual intent).
        return new C3Effect(models, slotName, effectiveAsb, effectiveAdb);
    }
    public C3Role? LoadRole(
        IEnumerable<(string MeshPath, string? TexturePath, uint RolePartId, int Asb, int Adb)> parts)
    {
        var role = new C3Role();
        foreach (var (meshPath, texturePath, rolePartId, asb, adb) in parts)
        {
            string slot = "Body";
            var p = LoadPart(meshPath, texturePath, slot, rolePartId, asb, adb);
            if (p != null) role.AssignSlot(p);
        }

        return (role.Body != null) ? role : null;
    }

    public C3Role? LoadRole(IEnumerable<PartDescriptor> descriptors)
    {
        var role = new C3Role();
        foreach (var desc in descriptors)
        {
            var p = LoadPart(desc.MeshPath, desc.TexturePath, desc.SlotName, desc.RolePartId, desc.Asb, desc.Adb);
            if (p != null) role.AssignSlot(p);
        }
        return (role.Body != null) ? role : null;
    }

    // ── Legacy merge API ──────────────────────────────────────────────────
    public (C3Model? Model, int PartCount) LoadAndMerge(
        IEnumerable<(string MeshPath, string? TexturePath, int Asb, int Adb)> parts)
    {
        C3Model? merged = null;
        int partCount = 0;

        foreach (var (meshPath, texturePath, asb, adb) in parts)
        {
            var part = LoadModel(meshPath);
            if (part == null) continue;

            int effectiveAsb = asb > 0 ? asb : 5;
            int effectiveAdb = adb > 0 ? adb : 6;

            if (!string.IsNullOrEmpty(texturePath))
                BindTextureToPart(part, texturePath);

            ApplyBlend(part, effectiveAsb, effectiveAdb);

            if (merged == null)
            {
                merged = part;
            }
            else
            {
                foreach (var phy in part.Phys) merged.Phys.Add(phy);
                foreach (var shape in part.Shapes) merged.Shapes.Add(shape);
                foreach (var ptcl in part.Ptcls) merged.Ptcls.Add(ptcl);
                foreach (var scene in part.Scenes) merged.Scenes.Add(scene);
                foreach (var mot in part.Motions) merged.Motions.Add(mot);
                foreach (var smot in part.SMotions) merged.SMotions.Add(smot);
            }

            partCount++;
        }

        return (merged, partCount);
    }

    // ── Texture ───────────────────────────────────────────────────────────

    public int LoadTexture(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return -1;

        // 1 – cache hit (also increments DupCount)
        int cached = C3Texture.Texture_Load(relativePath);
        if (cached >= 0) return cached;

        try
        {
            if (_assets != null)
            {
                using var stream = _assets.Open(relativePath);
                var tex = DecodeTexture(stream, relativePath);
                return C3Texture.Texture_Load(relativePath, tex);
            }
            return -1;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[C3AssetLoader] LoadTexture '{relativePath}': {ex.Message}");
            return -1;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────
    private static void ApplyBlend(C3Model model, int asb, int adb)
    {
        foreach (var phy in model.Phys) { phy.BlendAsb = asb; phy.BlendAdb = adb; }
        foreach (var shape in model.Shapes) { shape.BlendAsb = asb; shape.BlendAdb = adb; }
        foreach (var ptcl in model.Ptcls) { ptcl.BlendAsb = asb; ptcl.BlendAdb = adb; }
        foreach (var scene in model.Scenes) { scene.BlendAsb = asb; scene.BlendAdb = adb; }
    }

    private void BindTextureToPart(C3Model model, string texturePath)
    {
        // Count total chunks that will hold a reference
        int totalRefs = model.Phys.Count + model.Shapes.Count
                      + model.Ptcls.Count + model.Scenes.Count;
        if (totalRefs == 0) return;

        // First load → DupCount = 1 (or DupCount++ if already cached)
        int idx = LoadTexture(texturePath);
        if (idx < 0) return;

        // Acquire remaining refs — one per additional chunk
        for (int i = 1; i < totalRefs; i++)
            C3Texture.Texture_Load(texturePath);  // DupCount++

        // Assign the slot index to every chunk
        var entry = C3Texture.Get(idx);
        foreach (var phy in model.Phys)
        {
            phy.TexIndex = idx;
            phy.GpuTexture = entry?.Texture;   // refresh GPU pointer post-load
        }
        foreach (var shape in model.Shapes) shape.TexIndex = idx;
        foreach (var ptcl in model.Ptcls) ptcl.TexIndex = idx;
        foreach (var scene in model.Scenes) scene.TexIndex = idx;
    }

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