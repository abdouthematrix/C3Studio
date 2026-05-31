using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace C3Studio.Infrastructure.Rendering;

public sealed class C3Renderer : IDisposable
{
    private readonly GraphicsDevice _gd;
    private C3Role? _role;
    private C3Effect? _effect;
    private double _frameTimer;
    private double _secondsPerFrame = 1.0 / 30.0;

    public bool IsPlaying { get; set; } = true;

    public float Fps
    {
        get => (float)(1.0 / _secondsPerFrame);
        set => _secondsPerFrame = value > 0 ? 1.0 / value : 1.0 / 30.0;
    }

    public C3Role? Role => _role;
    public C3Effect? Effect => _effect;

    // ── Frame state (role takes priority; falls back to standalone effect) ─
    public int MaxFrameCount => Math.Max(_role?.MaxFrameCount ?? 0, _effect?.MaxFrameCount ?? 0);
    public int CurrentFrame => _role?.CurrentFrame ?? _effect?.CurrentFrame ?? 0;

    // ── Mesh visibility (forwarded to role and/or effect) ─────────────────
    public IEnumerable<string> GetPhyNames() =>
        (_role?.GetPhyNames() ?? Enumerable.Empty<string>())
        .Concat(_effect?.GetPhyNames() ?? Enumerable.Empty<string>());

    public bool GetPhyVisibility(string name) =>
        _role?.GetPhyVisibility(name) ?? _effect?.GetPhyVisibility(name) ?? true;

    public void SetPhyVisibility(string name, bool visible)
    {
        _role?.SetPhyVisibility(name, visible);
        _effect?.SetPhyVisibility(name, visible);
    }

    public C3Renderer(GraphicsDevice gd) => _gd = gd;

    // ── Role loading ──────────────────────────────────────────────────────

    /// <summary>
    /// Installs a new <see cref="C3Role"/>, initialises GPU resources for every
    /// part, and runs the first Calculate + upload cycle before the first Draw.
    ///
    /// Mirrors the old <c>C3Renderer.LoadModelDirect</c> sequence:
    ///   Calculate → Initialize(_gd) → Update
    /// applied once per <see cref="C3RolePart"/> in the role.
    /// </summary>
    public void LoadRole(C3Role role)
    {
        Unload();
        _role = role;
        _frameTimer = 0;

        // Prime skinning: socket binding runs inside Calculate so bone positions
        // are valid before Initialize uploads the first vertex batch.
        _role.Calculate();
        _role.UpdateShapes();
        _role.Initialize(_gd);
        // Upload OutputVertices that Calculate just produced.
        _role.UploadAllVertices();
    }

    // ── Effect loading ────────────────────────────────────────────────────

    /// <summary>
    /// Installs a standalone <see cref="C3Effect"/> (an Effect asset node with no
    /// body mesh), primes GPU resources, and runs the first Calculate + upload cycle.
    /// Any previously loaded role or effect is unloaded first.
    /// </summary>
    public void LoadEffect(C3Effect effect)
    {
        Unload();
        _effect = effect;
        _frameTimer = 0;

        _effect.Calculate();
        _effect.UpdateShapes();
        _effect.Initialize(_gd);
        _effect.UploadVertices();
    }

    /// <summary>Disposes and clears the standalone effect without touching the role.</summary>
    public void UnloadEffect()
    {
        _effect?.Dispose();
        _effect = null;
    }

    // ── Motion swap ───────────────────────────────────────────────────────

    /// <summary>
    /// Replaces the Body (and BodyExtras) animation.
    /// Resets the frame timer so the new motion begins from frame 0.
    /// </summary>
    public void ChangeMotion(Stream stream)
    {
        if (_role == null) return;
        _frameTimer = 0;
        _role.ChangeMotion(stream);
        _role.Calculate();
        _role.UpdateShapes();
        _role.UploadAllVertices();
    }

    // ── Update ────────────────────────────────────────────────────────────

    /// <summary>
    /// Advances animation by as many frames as elapsed time warrants,
    /// runs socket-binding Calculate, then uploads dirty vertices to the GPU.
    /// </summary>
    public void Update(GameTime gameTime)
    {
        if (_role == null && _effect == null) return;

        if (IsPlaying)
        {
            _frameTimer += gameTime.ElapsedGameTime.TotalSeconds;
            while (_frameTimer >= _secondsPerFrame)
            {
                _role?.AdvanceFrame(1);
                _role?.Calculate();         // includes socket binding
                _role?.UpdateShapes();
                _effect?.AdvanceFrame(1);
                _effect?.Calculate();
                _effect?.UpdateShapes();
                _frameTimer -= _secondsPerFrame;
            }
        }

        _role?.Update();                    // GPU upload (respects visibility flags)
        _effect?.Update();
    }

    // ── Draw ──────────────────────────────────────────────────────────────

    public void Draw(Matrix view, Matrix projection)
    {
        _role?.Draw(_gd, view, projection);
        _effect?.Draw(_gd, view, projection);
    }

    // ── Frame stepping ────────────────────────────────────────────────────

    /// <summary>Advances or rewinds by <paramref name="delta"/> frames and force-uploads.</summary>
    public void StepFrame(int delta)
    {
        if (_role == null && _effect == null) return;
        _role?.AdvanceFrame(delta);
        _role?.Calculate();
        _role?.UpdateShapes();
        _role?.UploadAllVertices();
        _effect?.AdvanceFrame(delta);
        _effect?.Calculate();
        _effect?.UpdateShapes();
        _effect?.UploadVertices();
    }

    public void ResetFrame()
    {
        if (_role == null && _effect == null) return;
        _role?.SetFrame(0);
        _role?.Calculate();
        _role?.UpdateShapes();
        _role?.UploadAllVertices();
        _effect?.SetFrame(0);
        _effect?.Calculate();
        _effect?.UpdateShapes();
        _effect?.UploadVertices();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    public void Unload()
    {
        _role?.Dispose();
        _role = null;
        UnloadEffect();
    }

    public void Dispose() => Unload();
}