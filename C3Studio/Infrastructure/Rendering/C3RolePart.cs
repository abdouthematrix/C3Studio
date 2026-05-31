using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using C3Studio.Infrastructure.C3Format;

namespace C3Studio.Infrastructure.Rendering;

public sealed class C3RolePart : IDisposable
{
    public string SlotName { get; }
    public int BlendAsb { get; set; } = 5;   // D3DBLEND_SRCALPHA
    public int BlendAdb { get; set; } = 6;   // D3DBLEND_INVSRCALPHA
    public List<C3Effect> Effects { get; } = new();
    // ── Inner model ───────────────────────────────────────────────────────
    public C3Model Model => _model;
    private readonly C3Model _model;
    // ── Frame state (delegated to model) ──────────────────────────────────
    public int MaxFrameCount => _model.MaxFrameCount;
    public int CurrentFrame => PeekCurrentFrame();

    // ── Constructor ───────────────────────────────────────────────────────
    public C3RolePart(C3Model model, string slotName, int asb = 5, int adb = 6)
    {
        _model = model;
        SlotName = slotName;
        BlendAsb = asb;
        BlendAdb = adb;
    }
    public C3Motion? GetVirtualMotion(string name) =>
        _model.GetVirtualMotion(name);
    public void SetVirtualMotion(C3Motion? socketMotion)
    {
        if (socketMotion != null)
            _model.SetVirtualMotion(socketMotion);
    }
    public void MultiplyPhy(Matrix matrix)
    {
        foreach (var phy in _model.Phys)
            phy.Multiply(-1, matrix);
    }
    public void ClearMatrix()
    {
        foreach (var phy in _model.Phys)
            phy.ClearMatrix();
    }
    // ── Per-frame compute ─────────────────────────────────────────────────
    public void AdvanceFrame(int step)
    {
        _model.AdvanceFrame(step);
        foreach (var e in Effects) e.AdvanceFrame(step);
    }
    public void SetFrame(int frame)
    {
        _model.SetFrame(frame);
        foreach (var e in Effects) e.SetFrame(frame);
    }
    public void Calculate()
    {
        _model.Calculate();
        foreach (var e in Effects)
        {
            e.Bind(this);   // snap effect to body root bone
            e.Calculate();  // skin against that matrix
        }
    }
    public void UpdateShapes()
    {
        _model.UpdateShapes();
        foreach (var e in Effects) e.UpdateShapes();
    }
    public void Update()
    {
        _model.Update();
        foreach (var e in Effects) e.Update();
    }
    public void UploadVertices()
    {
        _model.UploadAllPhyVertices();
        foreach (var e in Effects) e.UploadVertices();
    }
    // ── Motion swap ───────────────────────────────────────────────────────
    public void ChangeMotion(Stream stream)
    {
        _model.ChangeMotion(stream);
        _model.Calculate();
        _model.UploadAllPhyVertices();
    }

    public void Initialize(GraphicsDevice gd)
    {
        _model.Initialize(gd);
        foreach (var e in Effects) e.Initialize(gd);
    }
    public void Draw(GraphicsDevice gd, Matrix view, Matrix projection)
    {
        _model.Draw(gd, view, projection);
        foreach (var e in Effects) e.Draw(gd, view, projection);
    }
    // ── Visibility ────────────────────────────────────────────────────────
    public IEnumerable<string> GetPhyNames() => _model.GetPhyNames();
    public bool GetPhyVisibility(string name) => _model.GetPhyVisibility(name);
    public void SetPhyVisibility(string name, bool visible) => _model.SetPhyVisibility(name, visible);

    // ── Lifecycle ─────────────────────────────────────────────────────────
    public void Dispose()
    {
        _model.Unload();
        foreach (var e in Effects) e.Dispose();
        Effects.Clear();
    }
    // ── Private ───────────────────────────────────────────────────────────
    private int PeekCurrentFrame()
    {
        if (_model.Motions.Count > 0) return _model.Motions[0].CurrentFrame;
        if (_model.Shapes.Count > 0 && _model.Shapes[0].Motion != null) return _model.Shapes[0].Motion!.CurrentFrame;
        if (_model.Ptcls.Count > 0) return _model.Ptcls[0].CurrentFrame;
        if (_model.Scenes.Count > 0) return _model.Scenes[0].CurrentFrame;
        return 0;
    }
}