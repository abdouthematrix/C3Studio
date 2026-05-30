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

    public IEnumerable<string> GetPhyNames() => _model.GetPhyNames();
    public bool GetPhyVisibility(string name) => _model.GetPhyVisibility(name);
    public void SetPhyVisibility(string name, bool visible) => _model.SetPhyVisibility(name, visible);
    // ─────────────────────────────────────────────────────────────────────
    public bool IsPlaying { get; set; } = true;
    public float Fps
    {
        get => (float)(1.0 / _secondsPerFrame);
        set => _secondsPerFrame = value > 0 ? 1.0 / value : 1.0 / 30.0;
    }
    public C3Model? Model => _model;

    public C3Renderer(GraphicsDevice gd) { _gd = gd; }

    // ── Model loading ─────────────────────────────────────────────────────    
    public void LoadModelDirect(C3Model model)
    {        
        _model = model;
        _model.Calculate();
        _model.Initialize(_gd);
    }
    
    // ── Motion ────────────────────────────────────────────────────────────  
    public void ChangeMotion(Stream stream)
    {
        if (_model == null) return;
        _frameTimer = 0;
        _model.ChangeMotion(stream);
        _model.Calculate();
        _model.UploadAllPhyVertices();
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
        _model.Update();
    }

    // ── Draw ──────────────────────────────────────────────────────────────
    public void Draw(Matrix view, Matrix projection)
    {
        if (_model == null) return;        
        _model.Draw(_gd, view, projection);        
    }
    // ── Frame control ─────────────────────────────────────────────────────
    public void StepFrame(int delta)
    {
        if (_model == null) return;
        _model.AdvanceFrame(delta);
        _model.Calculate();
        _model.UpdateShapes();
        _model.UploadAllPhyVertices();
    }

    public void ResetFrame()
    {
        if (_model == null) return;
        _model.SetFrame(0);
        _model.Calculate();
        _model.UploadAllPhyVertices();
    }
    public void Unload()
    {
        if (_model != null)
        {
            _model.Unload();           
            _model = null;
        }
    }
    // ── IDisposable ───────────────────────────────────────────────────────
    public void Dispose() => Unload();
}