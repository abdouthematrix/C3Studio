using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using C3Studio.Infrastructure.C3Format;

namespace C3Studio.Infrastructure.Rendering;

// ── GPU resources for one C3Phy mesh ─────────────────────────────────────────
public class PhyRenderData : IDisposable
{
    public DynamicVertexBuffer? VertexBuffer { get; private set; }
    public IndexBuffer?         IndexBuffer  { get; private set; }
    public Texture2D?           Texture      { get; set; }
    public C3Phy                Phy          { get; set; }

    public PhyRenderData(GraphicsDevice gd, C3Phy phy) { Phy=phy; Rebuild(gd); }

    public void Rebuild(GraphicsDevice gd)
    {
        VertexBuffer?.Dispose(); IndexBuffer?.Dispose();
        VertexBuffer=null; IndexBuffer=null;
        if (Phy.TotalVertexCount==0||Phy.TotalIndexCount==0) return;
        VertexBuffer=new DynamicVertexBuffer(gd,
            VertexPositionColorTexture.VertexDeclaration,
            Phy.TotalVertexCount, BufferUsage.WriteOnly);
        IndexBuffer=new IndexBuffer(gd,IndexElementSize.SixteenBits,
            Phy.TotalIndexCount, BufferUsage.WriteOnly);
        IndexBuffer.SetData(Phy.IndexBuffer.ToArray());
        UploadVertices();
    }

    public void UploadVertices()
    {
        if (VertexBuffer==null) return;
        var v=Phy.BuildGpuVertices();
        if (v.Length>0) VertexBuffer.SetData(v,0,v.Length,SetDataOptions.Discard);
    }

    public void Dispose() { VertexBuffer?.Dispose(); IndexBuffer?.Dispose(); }
}

// ── C3Renderer ────────────────────────────────────────────────────────────────
/// <summary>
/// Renders all C3 chunk types in correct order: SCEN → PHY (opaque) → PHY (alpha) → PTCL → SHAP.
/// PHY material tint applied via effect.DiffuseColor/Alpha.
/// Blend mode mapped from D3D values: 5/6=AlphaBlend, 2/2=Additive.
/// Culling: D3DCULL_CW → CullCounterClockwise; 2SID tag → CullNone.
/// </summary>
public class C3Renderer : IDisposable
{
    private readonly GraphicsDevice      _gd;
    private readonly BasicEffect         _effect;
    private C3Model?                     _model;
    private readonly List<PhyRenderData> _phyData = new();

    private double _frameTimer;
    private double _secondsPerFrame = 1.0 / 30.0;

    public bool    IsPlaying { get; set; } = true;
    public float   Fps
    {
        get => (float)(1.0 / _secondsPerFrame);
        set => _secondsPerFrame = value > 0 ? 1.0 / value : 1.0 / 30.0;
    }
    public Matrix  World { get; set; } = Matrix.Identity;
    public C3Model? Model => _model;

    public C3Renderer(GraphicsDevice gd)
    {
        _gd    = gd;
        _effect = new BasicEffect(gd)
        { LightingEnabled=false, VertexColorEnabled=true, TextureEnabled=true };
    }

    // ------------------------------------------------------------------
    public void LoadModel(string c3FilePath, string? texturePath=null, Matrix? worldRotation=null)
    {
        Unload();
        _model = C3Model.Load(c3FilePath, gd:_gd);
        _model.PhyReplaced += OnPhyReplaced;

        if (worldRotation.HasValue)
            foreach (var phy in _model.Phys)
                if (phy.Motion!=null)
                { phy.Motion.ClearMatrix(); phy.Motion.Multiply(-1, worldRotation.Value); }

        _model.Calculate();

        string dir  = Path.GetDirectoryName(c3FilePath) ?? string.Empty;
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
            foreach (var phy in _model.Phys)
                if (phy.Motion != null)
                { phy.Motion.ClearMatrix(); phy.Motion.Multiply(-1, worldRotation.Value); }
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
        var tex = LoadTexture(texturePath);       // already exists, private → keep or make internal
        foreach (var rd in _phyData)
            rd.Texture = tex;
    }
    public void OverrideTexture(Texture2D texture2D)
    {
        foreach (var rd in _phyData)
            rd.Texture = texture2D;
    }

    private void Unload()
    {
        if (_model != null) _model.PhyReplaced -= OnPhyReplaced;
        foreach (var rd in _phyData) rd.Dispose();
        _phyData.Clear();
        _model = null;
    }

    // ------------------------------------------------------------------
    public void ChangeMotion(string motionFilePath, Matrix? worldRotation=null)
    {
        if (_model==null) return;
        _frameTimer=0;
        _model.ChangeMotion(motionFilePath, worldRotation ?? Matrix.Identity);
        _model.Calculate();
        foreach (var rd in _phyData) if(rd.Phy.Draw&&rd.VertexBuffer!=null) rd.UploadVertices();
    }
    // New — accepts a pre-opened stream
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
        if (_model==null) return;
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
            if (rd.Phy.Draw && rd.VertexBuffer!=null) rd.UploadVertices();
    }

    // ------------------------------------------------------------------
    public void Draw(Matrix view, Matrix projection)
    {
        if (_model==null) return;
        _gd.SamplerStates[0] = SamplerState.LinearWrap;
        DrawScene(view, projection);
        DrawPhy(view, projection);
        DrawPtcl(view, projection);
        DrawShape(view, projection);
    }

    private void DrawScene(Matrix view, Matrix projection)
    {
        if (_model!.Scenes.Count==0) return;
        _effect.VertexColorEnabled=false; _effect.LightingEnabled=false;
        foreach (var scene in _model.Scenes) scene.Draw(_gd, _effect, view, projection);
    }

    private void DrawPhy(Matrix view, Matrix projection)
    {
        if (_phyData.Count==0) return;
        _gd.DepthStencilState=DepthStencilState.Default;
        _effect.View=view; _effect.Projection=projection; _effect.World=World;
        _effect.VertexColorEnabled=true; _effect.LightingEnabled=false;

        // Opaque pass
        _gd.BlendState=BlendState.Opaque;
        foreach (var rd in _phyData)
        {
            var phy=rd.Phy;
            if (!phy.Draw||phy.NormalTriCount==0||!phy.IsFullyOpaque||rd.VertexBuffer==null) continue;
            _gd.RasterizerState=phy.TwoSided?RasterizerState.CullNone:RasterizerState.CullCounterClockwise;
            SetPhyEffect(rd);
            _gd.SetVertexBuffer(rd.VertexBuffer); _gd.Indices=rd.IndexBuffer;
            foreach (var pass in _effect.CurrentTechnique.Passes)
            { pass.Apply(); _gd.DrawIndexedPrimitives(PrimitiveType.TriangleList,0,0,phy.NormalTriCount); }
        }

        // Alpha pass
        _gd.RasterizerState=RasterizerState.CullNone;
        foreach (var rd in _phyData)
        {
            var phy=rd.Phy;
            if (!phy.Draw||rd.VertexBuffer==null) continue;
            bool tn=phy.NormalTriCount>0&&!phy.IsFullyOpaque;
            bool at=phy.AlphaTriCount>0;
            if (!tn&&!at) continue;
            _gd.BlendState=ResolveBlendState(phy.BlendAsb, phy.BlendAdb);
            SetPhyEffect(rd);
            _gd.SetVertexBuffer(rd.VertexBuffer); _gd.Indices=rd.IndexBuffer;
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                if (tn) _gd.DrawIndexedPrimitives(PrimitiveType.TriangleList,0,0,phy.NormalTriCount);
                if (at) _gd.DrawIndexedPrimitives(PrimitiveType.TriangleList,0,phy.AlphaIndexStart,phy.AlphaTriCount);
            }
        }
    }

    private void DrawPtcl(Matrix view, Matrix projection)
    {
        if (_model!.Ptcls.Count==0) return;
        foreach (var p in _model.Ptcls) p.Draw(_gd, _effect, view, projection, BlendState.AlphaBlend);
    }

    private void DrawShape(Matrix view, Matrix projection)
    {
        if (_model!.Shapes.Count==0) return;
        foreach (var s in _model.Shapes) s.Draw(_gd, _effect, view, projection);
    }

    // ------------------------------------------------------------------
    public void StepFrame(int delta)
    {
        if (_model==null) return;
        _model.AdvanceFrame(delta);
        _model.Calculate();
        _model.UpdateShapes();
        foreach (var rd in _phyData)
            if (rd.Phy.Draw&&rd.VertexBuffer!=null) rd.UploadVertices();
    }

    public void ResetFrame()
    {
        if (_model==null) return;
        _model.SetFrame(0);
        _model.Calculate();
        foreach (var rd in _phyData)
            if (rd.Phy.Draw&&rd.VertexBuffer!=null) rd.UploadVertices();
    }

    // ------------------------------------------------------------------
    private void SetPhyEffect(PhyRenderData rd)
    {
        var phy=rd.Phy; bool ht=rd.Texture!=null;
        _effect.TextureEnabled=ht; _effect.VertexColorEnabled=true;
        _effect.Texture=ht?rd.Texture:null;
        _effect.DiffuseColor=new Vector3(phy.R, phy.G, phy.B);
        _effect.Alpha=phy.Alpha;
    }

    // 5=SrcAlpha, 6=InvSrcAlpha (AlphaBlend) · 2=One/One (Additive)
    private static BlendState ResolveBlendState(int asb, int adb) =>
        (asb==2 && adb==2) ? BlendState.Additive : BlendState.AlphaBlend;

    private Texture2D? ResolvePhyTexture(C3Phy phy, string? explicitPath, string dir, string baseName)
    {
        if (explicitPath!=null && File.Exists(explicitPath)) return LoadTexture(explicitPath);
        if (phy.TexIndex!=-1) { var c=C3Texture.Get(phy.TexIndex)?.Texture; if(c!=null) return c; }
        string? found = FindTexture(dir, baseName)
                     ?? FindTexture(dir, Path.GetFileNameWithoutExtension(phy.TexName));
        return found!=null ? LoadTexture(found) : null;
    }

    private static string? FindTexture(string dir, string? baseName)
    {
        if (string.IsNullOrEmpty(dir)||string.IsNullOrEmpty(baseName)) return null;
        foreach (var ext in new[]{".dds",".tga",".png",".jpg"})
        { string p=Path.Combine(dir,baseName+ext); if(File.Exists(p)) return p; }
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
                _      => LoadStream(path)
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[C3Renderer] '{path}': {ex.Message}");
            var t=new Texture2D(_gd,1,1); t.SetData(new[]{Color.Magenta}); return t;
        }
    }

    private Texture2D LoadStream(string p)
    { using var s=File.OpenRead(p); return Texture2D.FromStream(_gd,s); }

    private void OnPhyReplaced(int slot)
    {
        if (slot<0||slot>=_phyData.Count) return;
        var rd=_phyData[slot]; var phy=_model!.Phys[slot];
        rd.Phy=phy; rd.Rebuild(_gd);
        rd.Texture= C3Texture.Get(phy.TexIndex)?.Texture;
    }

    public void Dispose()
    {
        Unload();
        _effect?.Dispose();
    }
}
