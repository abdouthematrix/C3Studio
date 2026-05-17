using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using C3Studio.Infrastructure.C3Format;

namespace C3Studio.Infrastructure.Rendering;

/// <summary>
/// Renders all C3 chunk types in correct order:
///   SCEN → PHY (opaque) → PHY (alpha) → PTCL → SHAP.
///
/// GPU lifecycle is fully delegated to the individual chunk classes:
///   • <see cref="C3Phy"/>   owns its DynamicVertexBuffer, IndexBuffer, and both effects.
///   • <see cref="C3Ptcl"/>  owns its AlphaTestEffect (created lazily on first Draw).
///   • <see cref="C3Scene"/> owns its VertexBuffer, IndexBuffer, and AlphaTestEffect.
///   • <see cref="C3Shape"/> owns its AlphaTestEffect (created lazily on first Draw).
///
/// Blend-state mapping is centralised in <see cref="C3BlendHelper"/>.
///
/// Equipment slots are filtered here (renderer policy), not inside the chunk classes.
/// Culling is also determined per-PHY by <see cref="C3Phy.DrawNormal"/> /
/// <see cref="C3Phy.DrawAlpha"/> based on the TwoSided flag.
/// </summary>
public class C3Renderer : IDisposable
{
    private readonly GraphicsDevice _gd;
    private C3Model? _model;

    private double _frameTimer;
    private double _secondsPerFrame = 1.0 / 30.0;

    // ── Equipment slot blacklist (mirrors C++ Draw/DrawAlpha skip-list) ───
    // Note: _V_ARMET="v_head" and _V_HEAD="v_armet" are intentionally swapped,
    // faithfully replicating the naming inconsistency in the original C++ constants.
    private static readonly HashSet<string> EquipmentSlots = new(
        ["v_head", "v_misc", "v_l_weapon", "v_r_weapon",
         "v_l_shield", "v_r_shield", "v_l_shoe", "v_r_shoe",
         "v_pet", "v_back", "v_armet",
         "v_l_arm", "v_r_arm", "v_l_leg", "v_r_leg", "v_mantle"],
        StringComparer.OrdinalIgnoreCase);

    private bool IsMeshVisible(C3Phy phy) => !EquipmentSlots.Contains(phy.Name);

    // ─────────────────────────────────────────────────────────────────────
    public bool IsPlaying { get; set; } = true;
    public float Fps
    {
        get => (float)(1.0 / _secondsPerFrame);
        set => _secondsPerFrame = value > 0 ? 1.0 / value : 1.0 / 30.0;
    }
    public Matrix World { get; set; } = Matrix.Identity;
    public C3Model? Model => _model;

    public C3Renderer(GraphicsDevice gd) { _gd = gd; }

    // ── Model loading ─────────────────────────────────────────────────────
    public void LoadModel(string c3FilePath, string? texturePath = null, Matrix? worldRotation = null)
    {
        Unload();
        _model = C3Model.Load(c3FilePath, gd: _gd);
        _model.PhyReplaced += OnPhyReplaced;

        ApplyWorldRotation(worldRotation);
        _model.Calculate();

        string dir = Path.GetDirectoryName(c3FilePath) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(c3FilePath);

        foreach (var phy in _model.Phys)
        {
            phy.InitializeGPU(_gd);
            phy.GpuTexture = ResolvePhyTexture(phy, texturePath, dir, name);
        }
        foreach (var scene in _model.Scenes) scene.UploadGPU(_gd);
    }

    public void LoadModelDirect(C3Model model, Matrix? worldRotation = null)
    {
        Unload();
        _model = model;
        _model.PhyReplaced += OnPhyReplaced;

        ApplyWorldRotation(worldRotation);
        _model.Calculate();

        foreach (var phy in _model.Phys)
        {
            phy.InitializeGPU(_gd);
            phy.GpuTexture = C3Texture.Get(phy.TexIndex)?.Texture;
        }
        foreach (var scene in _model.Scenes) scene.UploadGPU(_gd);
    }

    private void ApplyWorldRotation(Matrix? worldRotation)
    {
        if (_model == null || !worldRotation.HasValue) return;
        var rot = worldRotation.Value;

        foreach (var phy in _model.Phys)
            if (phy.Motion != null)
            { phy.ClearMatrix(); phy.Multiply(-1, rot); }

        foreach (var scene in _model.Scenes)
            scene.ExtraMatrix = scene.ExtraMatrix * rot;

        foreach (var ptc in _model.Ptcls)
            ptc.LocalMatrix = ptc.LocalMatrix * rot;

        foreach (var shape in _model.Shapes)
            if (shape.Motion != null)
            { shape.Motion.ClearMatrix(); shape.Motion.Multiply(rot); }
    }

    // ── Texture overrides ─────────────────────────────────────────────────
    /// <summary>Replaces every phy's render texture with a single file.</summary>
    public void OverrideTexture(string texturePath)
    {
        if (_model == null) return;
        var tex = LoadTexture(texturePath);
        foreach (var phy in _model.Phys) phy.GpuTexture = tex;
    }

    public void OverrideTexture(Texture2D texture)
    {
        if (_model == null) return;
        foreach (var phy in _model.Phys) phy.GpuTexture = texture;
    }

    /// <summary>
    /// Overrides the D3D blend factors for a single PHY slot.
    /// </summary>
    public void SetPhyBlend(int phyIndex, int asb, int adb)
    {
        if (_model == null || phyIndex < 0 || phyIndex >= _model.Phys.Count) return;
        _model.Phys[phyIndex].BlendAsb = asb;
        _model.Phys[phyIndex].BlendAdb = adb;
    }

    // ── Motion ────────────────────────────────────────────────────────────
    public void ChangeMotion(string motionFilePath, Matrix? worldRotation = null)
    {
        if (_model == null) return;
        _frameTimer = 0;
        _model.ChangeMotion(motionFilePath, worldRotation ?? Matrix.Identity);
        _model.Calculate();
        UploadAllPhyVertices();
    }

    public void ChangeMotion(Stream stream, Matrix? worldRotation = null)
    {
        if (_model == null) return;
        _frameTimer = 0;
        _model.ChangeMotion(stream, worldRotation ?? Matrix.Identity);
        _model.Calculate();
        UploadAllPhyVertices();
    }

    // ── Update ────────────────────────────────────────────────────────────
    public void Update(GameTime gameTime)
    {
        if (_model == null) return;
        if (IsPlaying)
        {
            _frameTimer += gameTime.ElapsedGameTime.TotalSeconds;
            while (_frameTimer >= _secondsPerFrame)
            {
                _model.AdvanceFrame(1);
                _model.Calculate();
                _model.UpdateShapes();
                _frameTimer -= _secondsPerFrame;
            }
        }
        foreach (var phy in _model.Phys)
            if (phy.Draw && IsMeshVisible(phy)) phy.UploadVertices();
    }

    // ── Draw ──────────────────────────────────────────────────────────────
    public void Draw(Matrix view, Matrix projection)
    {
        if (_model == null) return;
        _gd.SamplerStates[0] = SamplerState.LinearWrap;

        DrawScene(view, projection);
        DrawPhy(view, projection);
        DrawPtcl(view, projection);
        DrawShape(view, projection);
    }

    private void DrawScene(Matrix view, Matrix projection)
    {
        foreach (var scene in _model!.Scenes)
            scene.Draw(_gd, view, projection);
    }

    private void DrawPhy(Matrix view, Matrix projection)
    {
        if (_model!.Phys.Count == 0) return;

        // ── Opaque pass ────────────────────────────────────────────────────
        foreach (var phy in _model.Phys)
        {
            if (!IsMeshVisible(phy)) continue;
            phy.DrawNormal(_gd, view, projection, World);
        }

        // ── Alpha / semi-transparent pass ──────────────────────────────────
        foreach (var phy in _model.Phys)
        {
            if (!IsMeshVisible(phy)) continue;
            phy.DrawAlpha(_gd, view, projection, World, bZ: false);
        }
    }

    private void DrawPtcl(Matrix view, Matrix projection)
    {
        foreach (var p in _model!.Ptcls)
            p.Draw(_gd, view, projection);
    }

    private void DrawShape(Matrix view, Matrix projection)
    {
        foreach (var s in _model!.Shapes)
            s.Draw(_gd, view, projection);
    }

    // ── Frame control ─────────────────────────────────────────────────────
    public void StepFrame(int delta)
    {
        if (_model == null) return;
        _model.AdvanceFrame(delta);
        _model.Calculate();
        _model.UpdateShapes();
        UploadAllPhyVertices();
    }

    public void ResetFrame()
    {
        if (_model == null) return;
        _model.SetFrame(0);
        _model.Calculate();
        UploadAllPhyVertices();
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private void UploadAllPhyVertices()
    {
        if (_model == null) return;
        foreach (var phy in _model.Phys)
            if (phy.Draw) phy.UploadVertices();
    }

    private void Unload()
    {
        if (_model != null)
        {
            _model.PhyReplaced -= OnPhyReplaced;
            foreach (var phy in _model.Phys) phy.Dispose();
            foreach (var scene in _model.Scenes) scene.Dispose();
            foreach (var ptcl in _model.Ptcls) ptcl.Dispose();
            foreach (var shape in _model.Shapes) shape.Dispose();
            _model = null;
        }
    }

    private void OnPhyReplaced(int slot)
    {
        if (_model == null || slot < 0 || slot >= _model.Phys.Count) return;
        var phy = _model.Phys[slot];
        phy.Rebuild(_gd);
        phy.GpuTexture = C3Texture.Get(phy.TexIndex)?.Texture;
    }

    // ── Texture helpers ───────────────────────────────────────────────────
    private Texture2D? ResolvePhyTexture(C3Phy phy, string? explicitPath, string dir, string baseName)
    {
        if (explicitPath != null && File.Exists(explicitPath)) return LoadTexture(explicitPath);
        if (phy.TexIndex != -1) { var c = C3Texture.Get(phy.TexIndex)?.Texture; if (c != null) return c; }
        string? found = FindTexture(dir, baseName)
                     ?? FindTexture(dir, Path.GetFileNameWithoutExtension(phy.TexName));
        return found != null ? LoadTexture(found) : null;
    }

    private static string? FindTexture(string dir, string? baseName)
    {
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(baseName)) return null;
        foreach (var ext in new[] { ".dds", ".tga", ".png", ".jpg" })
        { string p = Path.Combine(dir, baseName + ext); if (File.Exists(p)) return p; }
        return null;
    }

    private Texture2D LoadTexture(string path)
    {
        try
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".dds" => DDSLoader.Load(_gd, path),
                ".tga" => TGALoader.Load(_gd, path),
                _ => LoadStream(path)
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[C3Renderer] '{path}': {ex.Message}");
            var t = new Texture2D(_gd, 1, 1); t.SetData(new[] { Color.Magenta }); return t;
        }
    }

    private Texture2D LoadStream(string p)
    { using var s = File.OpenRead(p); return Texture2D.FromStream(_gd, s); }

    // ── IDisposable ───────────────────────────────────────────────────────
    public void Dispose() => Unload();
}