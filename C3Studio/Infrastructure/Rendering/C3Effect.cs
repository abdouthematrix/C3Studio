using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using C3Studio.Infrastructure.C3Format;

namespace C3Studio.Infrastructure.Rendering;

public sealed class C3Effect : IDisposable
{
    public int BlendAsb { get; set; } = 5;   // D3DBLEND_SRCALPHA
    public int BlendAdb { get; set; } = 6;   // D3DBLEND_INVSRCALPHA

    // ── Inner models ──────────────────────────────────────────────────────
    public IReadOnlyList<C3Model> Models => _models;
    private readonly C3Model[] _models;

    // ── Frame state (delegated to first model that has animation) ─────────
    public int MaxFrameCount => _models.Length == 0 ? 0 : _models.Max(m => m.MaxFrameCount);
    public int CurrentFrame => PeekCurrentFrame();

    // ── Constructor ───────────────────────────────────────────────────────

    /// <summary>Multi-model constructor (e.g. an effect whose ini lists several .c3 slots).</summary>
    public C3Effect(IEnumerable<C3Model> models, string slotName, int asb = 5, int adb = 6)
    {
        _models = models.ToArray();
        BlendAsb = asb;
        BlendAdb = adb;
    }

    /// <summary>Single-model convenience constructor — kept for back-compat.</summary>
    public C3Effect(C3Model model, string slotName, int asb = 5, int adb = 6)
        : this([model], slotName, asb, adb) { }

    // ── Virtual-motion socket ──────────────────────────────────────────────

    /// <summary>Searches all models for a phy named <paramref name="name"/>.</summary>
    public C3Motion? GetVirtualMotion(string name)
    {
        foreach (var m in _models)
        {
            var motion = m.GetVirtualMotion(name);
            if (motion != null) return motion;
        }
        return null;
    }

    public void SetVirtualMotion(C3Motion? socketMotion)
    {
        if (socketMotion == null) return;
        foreach (var m in _models) m.SetVirtualMotion(socketMotion);
    }

    // ── Matrix helpers ────────────────────────────────────────────────────

    public void MultiplyPhy(Matrix matrix)
    {
        foreach (var m in _models)
            foreach (var phy in m.Phys)
                phy.Multiply(-1, matrix);
    }

    public void ClearMatrix()
    {
        foreach (var m in _models)
            foreach (var phy in m.Phys)
                phy.ClearMatrix();
    }

    // ── Bind (socket to body root bone) ──────────────────────────────────

    /// <summary>
    /// Copies the root-bone matrix of <paramref name="bodyPart"/>'s first phy
    /// into every Shape, Ptcl, and Phy across <b>all</b> models in this effect.
    ///
    /// Mirrors <c>C3DEffect::Bind(C3DObj* lpObj)</c>:
    /// <code>
    ///   CopyMemory(&shape->lpMotion->matrix,    &phy0->lpMotion->matrix[0], sizeof(matrix))
    ///   CopyMemory(&ptcl->matrix,               &phy0->lpMotion->matrix[0], sizeof(matrix))
    ///   CopyMemory(&effPhy->lpMotion->matrix[0], &phy0->lpMotion->matrix[0], sizeof(matrix))
    /// </code>
    /// </summary>
    public void Bind(C3RolePart bodyPart)
    {
        // ── Source: first phy's root bone matrix from the body part ───────
        var srcPhy = bodyPart.Model.Phys.Count > 0 ? bodyPart.Model.Phys[0] : null;
        if (srcPhy?.Motion == null) return;

        var srcMatrix = srcPhy.Motion.BoneMatrix.Count > 0
            ? srcPhy.Motion.BoneMatrix[0]
            : Matrix.Identity;

        foreach (var m in _models)
        {
            // Shapes: overwrite SMotion transform matrix; reset frame
            foreach (var shape in m.Shapes)
            {
                if (shape.Motion == null) continue;
                shape.Motion.Matrix = srcMatrix;
                shape.SetFrame(0);
            }

            // Ptcls: overwrite particle local matrix
            foreach (var ptcl in m.Ptcls)
                ptcl.LocalMatrix = srcMatrix;

            // Phys: overwrite bone[0] in each phy's motion
            foreach (var phy in m.Phys)
            {
                if (phy.Motion == null || phy.Motion.BoneMatrix.Count == 0) continue;
                phy.Motion.BoneMatrix[0] = srcMatrix;
            }
        }
    }

    // ── Per-frame compute ─────────────────────────────────────────────────

    public void AdvanceFrame(int step) { foreach (var m in _models) m.AdvanceFrame(step); }
    public void SetFrame(int frame) { foreach (var m in _models) m.SetFrame(frame); }
    public void Calculate() { foreach (var m in _models) m.Calculate(); }
    public void UpdateShapes() { foreach (var m in _models) m.UpdateShapes(); }
    public void Update() { foreach (var m in _models) m.Update(); }
    public void UploadVertices() { foreach (var m in _models) m.UploadAllPhyVertices(); }

    // ── Motion swap ───────────────────────────────────────────────────────

    public void ChangeMotion(Stream stream)
    {
        foreach (var m in _models)
        {
            stream.Seek(0, SeekOrigin.Begin);  // rewind so every model reads from the start
            m.ChangeMotion(stream);
            m.Calculate();
            m.UploadAllPhyVertices();
        }
    }

    // ── GPU ───────────────────────────────────────────────────────────────

    public void Initialize(GraphicsDevice gd)
    {
        foreach (var m in _models) m.Initialize(gd);
    }

    public void Draw(GraphicsDevice gd, Matrix view, Matrix projection)
    {
        foreach (var m in _models) m.Draw(gd, view, projection);
    }

    // ── Visibility ────────────────────────────────────────────────────────

    public IEnumerable<string> GetPhyNames() =>
        _models.SelectMany(m => m.GetPhyNames());

    public bool GetPhyVisibility(string name)
    {
        foreach (var m in _models)
            if (m.GetPhyNames().Contains(name, StringComparer.OrdinalIgnoreCase))
                return m.GetPhyVisibility(name);
        return true;
    }

    public void SetPhyVisibility(string name, bool visible)
    {
        foreach (var m in _models) m.SetPhyVisibility(name, visible);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    public void Dispose()
    {
        foreach (var m in _models) m.Unload();
    }

    // ── Private ───────────────────────────────────────────────────────────

    private int PeekCurrentFrame()
    {
        foreach (var m in _models)
        {
            if (m.Motions.Count > 0) return m.Motions[0].CurrentFrame;
            if (m.Shapes.Count > 0 && m.Shapes[0].Motion != null) return m.Shapes[0].Motion!.CurrentFrame;
            if (m.Ptcls.Count > 0) return m.Ptcls[0].CurrentFrame;
            if (m.Scenes.Count > 0) return m.Scenes[0].CurrentFrame;
        }
        return 0;
    }
}