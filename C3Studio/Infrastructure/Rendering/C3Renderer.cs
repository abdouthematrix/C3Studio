using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using C3Studio.Infrastructure.C3Format;

namespace C3Studio.Infrastructure.Rendering;

// ── GPU resources for one C3Phy mesh ─────────────────────────────────────────
public class PhyRenderData : IDisposable
{
    public DynamicVertexBuffer? VertexBuffer { get; private set; }
    public IndexBuffer? IndexBuffer { get; private set; }
    public Texture2D? Texture { get; set; }
    public C3Phy Phy { get; set; }

    public PhyRenderData(GraphicsDevice gd, C3Phy phy) { Phy = phy; Rebuild(gd); }

    public void Rebuild(GraphicsDevice gd)
    {
        VertexBuffer?.Dispose(); IndexBuffer?.Dispose();
        VertexBuffer = null; IndexBuffer = null;
        if (Phy.TotalVertexCount == 0 || Phy.TotalIndexCount == 0) return;
        VertexBuffer = new DynamicVertexBuffer(gd,
            VertexPositionColorTexture.VertexDeclaration,
            Phy.TotalVertexCount, BufferUsage.WriteOnly);
        IndexBuffer = new IndexBuffer(gd, IndexElementSize.SixteenBits,
            Phy.TotalIndexCount, BufferUsage.WriteOnly);
        IndexBuffer.SetData(Phy.IndexBuffer.ToArray());
        UploadVertices();
    }

    public void UploadVertices()
    {
        if (VertexBuffer == null) return;
        var v = Phy.BuildGpuVertices();
        if (v.Length > 0) VertexBuffer.SetData(v, 0, v.Length, SetDataOptions.Discard);
    }

    public void Dispose() { VertexBuffer?.Dispose(); IndexBuffer?.Dispose(); }
}

// ── C3Renderer ────────────────────────────────────────────────────────────────
/// <summary>
/// Renders all C3 chunk types in correct order: SCEN → PHY (opaque) → PHY (alpha) → PTCL → SHAP.
/// PHY material tint applied via effect.DiffuseColor/Alpha.
/// Blend mode mapped from D3D values using <see cref="ResolveBlendState"/>:
///   5/6 = SrcAlpha/InvSrcAlpha (AlphaBlend), 2/2 = One/One (Additive), etc.
/// Culling: D3DCULL_CW → CullCounterClockwise; 2SID tag → CullNone.
/// </summary>
public class C3Renderer : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly BasicEffect _effect;
    private readonly AlphaTestEffect _alphaTestEffect;
    private C3Model? _model;
    private readonly List<PhyRenderData> _phyData = new();

    private double _frameTimer;
    private double _secondsPerFrame = 1.0 / 30.0;

    // ── Blend-state cache: keyed by (d3dSrc, d3dDst) ─────────────────────
    // BlendState objects are GPU resources and must not be re-created each frame.
    private static readonly Dictionary<(int, int), BlendState> _blendCache = new();

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

    public C3Renderer(GraphicsDevice gd)
    {
        _gd = gd;
        _effect = new BasicEffect(gd)
        { LightingEnabled = false, VertexColorEnabled = true, TextureEnabled = true };
        _alphaTestEffect = new AlphaTestEffect(gd)
        {
            AlphaFunction = CompareFunction.GreaterEqual,
            ReferenceAlpha = 8,
        };
    }

    // ------------------------------------------------------------------
    public void LoadModel(string c3FilePath, string? texturePath = null, Matrix? worldRotation = null)
    {
        Unload();
        _model = C3Model.Load(c3FilePath, gd: _gd);
        _model.PhyReplaced += OnPhyReplaced;

        if (worldRotation.HasValue)
        {
            foreach (var phy in _model.Phys)
                if (phy.Motion != null)
                { phy.Motion.ClearMatrix(); phy.Motion.Multiply(-1, worldRotation.Value); }
            foreach (var scene in _model.Scenes)
                scene.ExtraMatrix = scene.ExtraMatrix * worldRotation.Value;
            foreach (var ptc in _model.Ptcls)
                ptc.LocalMatrix = ptc.LocalMatrix * worldRotation.Value;
            foreach (var shape in _model.Shapes)
                if (shape.Motion != null)
                { shape.Motion.ClearMatrix(); shape.Motion.Multiply(worldRotation.Value); }
        }

        _model.Calculate();

        string dir = Path.GetDirectoryName(c3FilePath) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(c3FilePath);

        foreach (var phy in _model.Phys)
        {
            var rd = new PhyRenderData(_gd, phy);
            rd.Texture = ResolvePhyTexture(phy, texturePath, dir, name);
            _phyData.Add(rd);
        }
    }

    public void LoadModelDirect(C3Model model, Matrix? worldRotation = null)
    {
        Unload();
        _model = model;
        _model.PhyReplaced += OnPhyReplaced;
        if (worldRotation.HasValue)
        {
            foreach (var phy in _model.Phys)
                if (phy.Motion != null)
                { phy.Motion.ClearMatrix(); phy.Motion.Multiply(-1, worldRotation.Value); }
            foreach (var scene in _model.Scenes)
                scene.ExtraMatrix = scene.ExtraMatrix * worldRotation.Value;
            foreach (var ptc in _model.Ptcls)
                ptc.LocalMatrix = ptc.LocalMatrix * worldRotation.Value;
            foreach (var shape in _model.Shapes)
                if (shape.Motion != null)
                { shape.Motion.ClearMatrix(); shape.Motion.Multiply(worldRotation.Value); }
        }
        _model.Calculate();
        foreach (var phy in _model.Phys)
        {
            var rd = new PhyRenderData(_gd, phy);
            rd.Texture = C3Texture.Get(phy.TexIndex)?.Texture;
            _phyData.Add(rd);
        }
    }

    /// <summary>
    /// Replaces every phy's texture with a single explicit file.
    /// Call after LoadModelDirect when the user has picked a texture override.
    /// </summary>
    public void OverrideTexture(string texturePath)
    {
        var tex = LoadTexture(texturePath);
        foreach (var rd in _phyData)
            rd.Texture = tex;
    }

    public void OverrideTexture(Texture2D texture2D)
    {
        foreach (var rd in _phyData)
            rd.Texture = texture2D;
    }

    /// <summary>
    /// Overrides the D3D blend factors for a single PHY slot.
    /// Useful for applying per-slot Asb/Adb after a multi-part merge.
    /// </summary>
    /// <param name="phyIndex">Index into the current model's Phys list.</param>
    /// <param name="asb">D3D source blend factor (e.g. 5 = SrcAlpha).</param>
    /// <param name="adb">D3D destination blend factor (e.g. 6 = InvSrcAlpha).</param>
    public void SetPhyBlend(int phyIndex, int asb, int adb)
    {
        if (_model == null || phyIndex < 0 || phyIndex >= _model.Phys.Count) return;
        _model.Phys[phyIndex].BlendAsb = asb;
        _model.Phys[phyIndex].BlendAdb = adb;
    }

    private void Unload()
    {
        if (_model != null) _model.PhyReplaced -= OnPhyReplaced;
        foreach (var rd in _phyData) rd.Dispose();
        _phyData.Clear();
        _model = null;
    }

    // ------------------------------------------------------------------
    public void ChangeMotion(string motionFilePath, Matrix? worldRotation = null)
    {
        if (_model == null) return;
        _frameTimer = 0;
        _model.ChangeMotion(motionFilePath, worldRotation ?? Matrix.Identity);
        _model.Calculate();
        foreach (var rd in _phyData) if (rd.Phy.Draw && rd.VertexBuffer != null) rd.UploadVertices();
    }

    public void ChangeMotion(Stream stream, Matrix? worldRotation = null)
    {
        if (_model == null) return;
        _frameTimer = 0;
        _model.ChangeMotion(stream, worldRotation ?? Matrix.Identity);
        _model.Calculate();
        foreach (var rd in _phyData) if (rd.Phy.Draw && rd.VertexBuffer != null) rd.UploadVertices();
    }

    // ------------------------------------------------------------------
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
        foreach (var rd in _phyData)
            if (rd.Phy.Draw && rd.VertexBuffer != null && IsMeshVisible(rd.Phy)) rd.UploadVertices();
    }

    // ------------------------------------------------------------------
    public void Draw(Matrix view, Matrix projection)
    {
        if (_model == null) return;
        _gd.SamplerStates[0] = SamplerState.LinearWrap;
        _alphaTestEffect.View = view;
        _alphaTestEffect.Projection = projection;
        _alphaTestEffect.World = World;
        _alphaTestEffect.VertexColorEnabled = true;

        _effect.View = view;
        _effect.Projection = projection;
        _effect.World = World;
        _effect.VertexColorEnabled = true;
        _effect.LightingEnabled = false;

        DrawScene(view, projection);
        DrawPhy(view, projection);
        DrawPtcl(view, projection);
        DrawShape(view, projection);
    }

    private void DrawScene(Matrix view, Matrix projection)
    {
        if (_model!.Scenes.Count == 0) return;
        _effect.VertexColorEnabled = false; _effect.LightingEnabled = false;
        foreach (var scene in _model.Scenes) scene.Draw(_gd, _alphaTestEffect, view, projection);
    }

    private void DrawPhy(Matrix view, Matrix projection)
    {
        if (_phyData.Count == 0) return;
        _gd.DepthStencilState = DepthStencilState.Default;
        // ── Opaque pass ────────────────────────────────────────────────────
        // AlphaTestEffect discards texture-transparent pixels (DXT1 cutouts)
        // without requiring a blend operation.
        _gd.BlendState = BlendState.Opaque;
        foreach (var rd in _phyData)
        {
            var phy = rd.Phy;
            if (!phy.Draw || phy.NormalTriCount == 0 || !phy.IsFullyOpaque || rd.VertexBuffer == null) continue;
            if (!IsMeshVisible(phy)) continue;
            _gd.RasterizerState = phy.TwoSided
                ? RasterizerState.CullNone
                : RasterizerState.CullCounterClockwise;
            SetPhyAlphaEffect(rd);
            _gd.SetVertexBuffer(rd.VertexBuffer);
            _gd.Indices = rd.IndexBuffer;
            foreach (var pass in _alphaTestEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, phy.NormalTriCount);
            }
        }

        // ── Alpha / semi-transparent pass ─────────────────────────────────
        // Per-PHY blend state resolved from D3D Asb/Adb values.
        foreach (var rd in _phyData)
        {
            var phy = rd.Phy;
            if (!phy.Draw || rd.VertexBuffer == null) continue;
            if (!IsMeshVisible(phy)) continue;
            bool tn = phy.NormalTriCount > 0 && !phy.IsFullyOpaque;
            bool at = phy.AlphaTriCount > 0;
            if (!tn && !at) continue;
            _gd.RasterizerState = phy.TwoSided
                ? RasterizerState.CullNone
                : RasterizerState.CullCounterClockwise;
            _gd.BlendState = ResolveBlendState(phy.BlendAsb, phy.BlendAdb);
            SetPhyAlphaEffect(rd);
            _gd.SetVertexBuffer(rd.VertexBuffer);
            _gd.Indices = rd.IndexBuffer;
            foreach (var pass in _alphaTestEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                if (tn) _gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, phy.NormalTriCount);
                if (at) _gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, phy.AlphaIndexStart, phy.AlphaTriCount);
            }
        }
    }

    private void DrawPtcl(Matrix view, Matrix projection)
    {
        if (_model!.Ptcls.Count == 0) return;
        foreach (var p in _model.Ptcls) p.Draw(_gd, _alphaTestEffect, view, projection, BlendState.AlphaBlend);
    }

    private void DrawShape(Matrix view, Matrix projection)
    {
        if (_model!.Shapes.Count == 0) return;
        foreach (var s in _model.Shapes) s.Draw(_gd, _alphaTestEffect, view, projection);
    }

    // ------------------------------------------------------------------
    public void StepFrame(int delta)
    {
        if (_model == null) return;
        _model.AdvanceFrame(delta);
        _model.Calculate();
        _model.UpdateShapes();
        foreach (var rd in _phyData)
            if (rd.Phy.Draw && rd.VertexBuffer != null) rd.UploadVertices();
    }

    public void ResetFrame()
    {
        if (_model == null) return;
        _model.SetFrame(0);
        _model.Calculate();
        foreach (var rd in _phyData)
            if (rd.Phy.Draw && rd.VertexBuffer != null) rd.UploadVertices();
    }

    // ------------------------------------------------------------------
    private void SetPhyEffect(PhyRenderData rd)
    {
        var phy = rd.Phy; bool ht = rd.Texture != null;
        _effect.TextureEnabled = ht; _effect.VertexColorEnabled = true;
        _effect.Texture = ht ? rd.Texture : null;
        _effect.DiffuseColor = new Vector3(phy.R, phy.G, phy.B);
        _effect.Alpha = phy.Alpha;
    }

    private void SetPhyAlphaEffect(PhyRenderData rd)
    {
        var phy = rd.Phy;
        _alphaTestEffect.VertexColorEnabled = true;
        _alphaTestEffect.Texture = rd.Texture;
        _alphaTestEffect.DiffuseColor = new Vector3(phy.R, phy.G, phy.B);
        _alphaTestEffect.Alpha = phy.Alpha;
    }

    // ── Blend state resolution ────────────────────────────────────────────
    // Maps D3D9 D3DBLEND_* integer constants to MonoGame Blend enum values,
    // then builds (and caches) a BlendState for the (src, dst) combination.
    //
    // D3DBLEND values:
    //   1=ZERO  2=ONE  3=SRCCOLOR  4=INVSRCCOLOR
    //   5=SRCALPHA  6=INVSRCALPHA  7=DESTALPHA  8=INVDESTALPHA
    //   9=DESTCOLOR  10=INVDESTCOLOR  11=SRCALPHASAT
    //
    // Common game combinations:
    //   (5,6) SrcAlpha / InvSrcAlpha  → standard alpha blend
    //   (2,2) One / One               → additive (full-bright)
    //   (5,2) SrcAlpha / One          → alpha-weighted additive (soft glow)
    private static BlendState ResolveBlendState(int asb, int adb)
    {
        var key = (asb, adb);
        if (_blendCache.TryGetValue(key, out var cached))
            return cached;

        var src = D3dBlend(asb);
        var dst = D3dBlend(adb);

        // Reuse MonoGame built-ins for the two most common cases to avoid
        // allocating extra GPU objects that are functionally identical.
        if (src == Blend.SourceAlpha && dst == Blend.InverseSourceAlpha)
            return _blendCache[key] = BlendState.AlphaBlend;

        var bs = new BlendState
        {
            ColorSourceBlend = src,
            ColorDestinationBlend = dst,
            AlphaSourceBlend = src,
            AlphaDestinationBlend = dst,
        };

        return _blendCache[key] = bs;
    }

    /// <summary>Converts a D3DBLEND integer constant to its MonoGame equivalent.</summary>
    private static Blend D3dBlend(int d3d) => d3d switch
    {
        1 => Blend.Zero,
        2 => Blend.One,
        3 => Blend.SourceColor,
        4 => Blend.InverseSourceColor,
        5 => Blend.SourceAlpha,
        6 => Blend.InverseSourceAlpha,
        7 => Blend.DestinationAlpha,
        8 => Blend.InverseDestinationAlpha,
        9 => Blend.DestinationColor,
        10 => Blend.InverseDestinationColor,
        11 => Blend.SourceAlphaSaturation,
        _ => Blend.One,   // unknown → safe fallback
    };

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

    private void OnPhyReplaced(int slot)
    {
        if (slot < 0 || slot >= _phyData.Count) return;
        var rd = _phyData[slot]; var phy = _model!.Phys[slot];
        rd.Phy = phy; rd.Rebuild(_gd);
        rd.Texture = C3Texture.Get(phy.TexIndex)?.Texture;
    }

    public void Dispose()
    {
        Unload();
        _effect?.Dispose();
        _alphaTestEffect?.Dispose();
    }
}