using C3Studio.Core.Models;
using C3Studio.Core.Services;
using C3Studio.Infrastructure.C3Format;
using C3Studio.Infrastructure.Loading;
using C3Studio.Infrastructure.Rendering;
using C3Studio.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Framework.WpfInterop;
using MonoGame.Framework.WpfInterop.Input;

namespace C3Studio.MonoGame;

public class C3StudioGame : WpfGame
{
    // ── DI / services ─────────────────────────────────────────────────────
    private IAssetFileService? _assetService;
    public IAssetFileService? AssetService
    {
        get => _assetService;
        set { _assetService = value; _loader?.SetAssetService(value!); }
    }

    // ── Events ────────────────────────────────────────────────────────────
    public event Action<int, int>? FrameChanged;
    public event Action? ModelLoaded;

    // ── Mesh visibility (forwarded through renderer → role) ───────────────
    public IEnumerable<string> GetMeshNames() => _renderer?.GetPhyNames() ?? [];
    public bool GetMeshVisibility(string name) => _renderer?.GetPhyVisibility(name) ?? true;
    public void SetMeshVisibility(string name, bool visible) => _renderer?.SetPhyVisibility(name, visible);

    /// <summary>
    /// Returns all phy names registered on the Body part of the currently loaded role.
    /// Includes socket attachment phys (e.g. "v_armet", "v_r_weapon") that are hidden
    /// by default but whose presence indicates the body supports that equipment slot.
    /// Returns an empty sequence when no role / body is loaded yet.
    /// </summary>
    public IEnumerable<string> GetBodyPhyNames() =>
        _renderer?.Role?.Body?.GetPhyNames() ?? [];

    // ── Playback ──────────────────────────────────────────────────────────
    public bool IsPlaying
    {
        get => _renderer?.IsPlaying ?? true;
        set { if (_renderer != null) _renderer.IsPlaying = value; }
    }

    public void SetFps(float fps) { if (_renderer != null) _renderer.Fps = fps; }
    public void StepFrame(int delta) => _renderer?.StepFrame(delta);
    public void ResetCamera() => _camera.Reset();

    // ── XNA services ──────────────────────────────────────────────────────
    private IGraphicsDeviceService? _gdService;
    private C3Renderer? _renderer;
    private C3AssetLoader? _loader;
    private BasicEffect? _gridEffect;
    private WpfMouse? _mouse;
    private WpfKeyboard? _keyboard;

    // ── Camera ────────────────────────────────────────────────────────────
    private readonly OrbitCamera _camera = new();
    private MouseState _prevMouse;

    // ── Grid ──────────────────────────────────────────────────────────────
    private VertexPositionColor[]? _gridVerts;

    // ── XNA init ──────────────────────────────────────────────────────────
    protected override void Initialize()
    {
        _gdService = new WpfGraphicsDeviceService(this);
        _mouse = new WpfMouse(this);
        _keyboard = new WpfKeyboard(this);
        base.Initialize();
    }

    protected override void LoadContent()
    {
        C3Texture.Initialize(GraphicsDevice);
        _loader = new C3AssetLoader(GraphicsDevice, _assetService);
        _renderer = new C3Renderer(GraphicsDevice);
        _gridEffect = new BasicEffect(GraphicsDevice)
        {
            VertexColorEnabled = true,
            LightingEnabled = false,
            TextureEnabled = false,
        };
        BuildGrid(halfSize: 20, step: 50f);
    }

    // ── Update / Draw ─────────────────────────────────────────────────────
    protected override void Update(GameTime gameTime)
    {
        HandleCamera();

        if (_renderer != null)
        {
            _renderer.Update(gameTime);     // no-ops internally when !IsPlaying
            FrameChanged?.Invoke(_renderer.CurrentFrame, _renderer.MaxFrameCount);
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Gray);

        float aspect = (float)Math.Max(1, GraphicsDevice.Viewport.Width)
                             / Math.Max(1, GraphicsDevice.Viewport.Height);

        var view = _camera.View;
        var projection = _camera.Projection(aspect);

        DrawGrid(view, projection);
        _renderer?.Draw(view, projection);

        base.Draw(gameTime);
    }

    // ── Public loading API ────────────────────────────────────────────────    
    public void LoadC3Parts(
        IEnumerable<(string MeshPath, string? TexturePath, uint RolePartId, int Asb, int Adb)> parts,
        string? motionPath = null)
    {
        if (_renderer == null || _loader == null) return;
        try
        {
            _renderer.Unload();

            var role = _loader.LoadRole(parts);
            if (role == null) return;

            _renderer.LoadRole(role);

            if (!string.IsNullOrEmpty(motionPath))
                ChangeMotion(motionPath);

            AutoFitCamera(role);
            ModelLoaded?.Invoke();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[C3StudioGame] LoadC3Parts: {ex.Message}");
        }
    }

    public void LoadC3Role(
    IEnumerable<PartDescriptor> slots,
    string? motionPath = null)
    {
        if (_renderer == null || _loader == null) return;
        try
        {
            _renderer.Unload();

            var role = _loader.LoadRole(slots);
            if (role == null) return;

            _renderer.LoadRole(role);

            role.BindAllParts();
            role.Calculate();
            role.UploadAllVertices();

            if (!string.IsNullOrEmpty(motionPath))
                ChangeMotion(motionPath);

            AutoFitCamera(role);
            ModelLoaded?.Invoke();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[C3StudioGame] LoadC3Role: {ex.Message}");
        }
    }
    // ── Static Part Attachment (Role Viewer) ──────────────────────────────
    public void AttachToRole(string slotName, IEnumerable<PartDescriptor> parts)
    {
        if (_renderer?.Role == null || _loader == null) return;

        // Remove existing attachment in this slot if any
        var oldPart = _renderer.Role.GetSlot(slotName);
        oldPart?.Dispose();

        foreach (var desc in parts)
        {
            var rolePart = _loader.LoadPart(desc.MeshPath, desc.TexturePath, slotName, desc.RolePartId, desc.Asb, desc.Adb);
            if (rolePart != null)
            {
                rolePart.Initialize(GraphicsDevice);
                _renderer.Role.AssignSlot(rolePart);
                break;
            }
        }

        _renderer.Role.BindAllParts();
        _renderer.Role.Calculate();
        _renderer.Role.UploadAllVertices();
    }

    public void DetachFromRole(string slotName)
    {
        if (_renderer?.Role == null) return;

        var part = _renderer.Role.GetSlot(slotName);
        if (part != null)
        {
            part.Dispose();
            _renderer.Role.ClearSlot(slotName);

            _renderer.Role.Calculate();
            _renderer.Role.UploadAllVertices();
        }
    }
    public void ChangeMotion(string relativePath)
    {
        if (_renderer == null || _assetService == null) return;
        try
        {
            using var stream = _assetService.Open(relativePath);
            _renderer.ChangeMotion(stream);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[C3StudioGame] ChangeMotion '{relativePath}': {ex.Message}");
        }
    }

    /// <summary>
    /// Loads all <see cref="EffectDescriptor"/> slots as a single multi-model
    /// <see cref="C3Effect"/> and attaches it to the Body part's
    /// <see cref="C3RolePart.Effects"/> list, replacing any previously bound effects.
    ///
    /// All descriptors are passed to <see cref="C3AssetLoader.LoadEffect(IEnumerable{EffectDescriptor},string)"/>
    /// so that an effect with several .c3 slots (Amount &gt; 1) becomes one
    /// <see cref="C3Effect"/> containing multiple <see cref="C3Model"/> instances.
    /// </summary>
    public void BindEffects(IEnumerable<EffectDescriptor> effects)
    {
        if (_renderer?.Role?.Body == null || _loader == null) return;
        var body = _renderer.Role.Body;

        // Dispose and clear any previously attached effects.
        foreach (var old in body.Effects) old.Dispose();
        body.Effects.Clear();

        var descriptors = effects.ToList();
        if (descriptors.Count == 0) return;

        try
        {
            var effect = _loader.LoadEffect(descriptors, slotName: "Effect");
            if (effect == null) return;

            // Prime skinning and upload initial vertices before first Draw.
            effect.Calculate();
            effect.Initialize(GraphicsDevice);
            effect.UploadVertices();

            body.Effects.Add(effect);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[C3StudioGame] BindEffects: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads an Effect asset node as a standalone <see cref="C3Effect"/> directly
    /// into the renderer — no body mesh required.  Any previously loaded role or
    /// effect is unloaded first.  Use this when <see cref="AssetData.Effects"/> is
    /// populated but <see cref="AssetData.MeshPaths"/> is empty.
    /// </summary>
    public void LoadStandaloneEffect(IEnumerable<EffectDescriptor> descriptors)
    {
        if (_renderer == null || _loader == null) return;
        try
        {
            _renderer.Unload();

            var effect = _loader.LoadEffect(descriptors, slotName: "Effect");
            if (effect == null) return;

            // LoadEffect primes Calculate / Initialize / UploadVertices internally.
            _renderer.LoadEffect(effect);
            ModelLoaded?.Invoke();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[C3StudioGame] LoadStandaloneEffect: {ex.Message}");
        }
    }

    // ── Camera input ──────────────────────────────────────────────────────
    private void HandleCamera()
    {
        var ms = _mouse!.GetState();
        var kb = _keyboard!.GetState();
        int dz = ms.ScrollWheelValue - _prevMouse.ScrollWheelValue;

        if (dz != 0) _camera.Zoom(dz * 0.01f);
        if (kb.IsKeyDown(Keys.W)) _camera.Zoom(0.05f);
        if (kb.IsKeyDown(Keys.S)) _camera.Zoom(-0.05f);

        float dx = ms.X - _prevMouse.X;
        float dy = ms.Y - _prevMouse.Y;

        bool leftHeld = ms.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Pressed;
        bool rightHeld = ms.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Pressed;
        bool midHeld = ms.MiddleButton == ButtonState.Pressed && _prevMouse.MiddleButton == ButtonState.Pressed;

        if (leftHeld) _camera.Orbit(dx * 0.005f, dy * 0.005f);
        if (rightHeld || midHeld) _camera.Pan(dx, dy);

        _prevMouse = ms;
    }

    // ── Grid ──────────────────────────────────────────────────────────────
    private void BuildGrid(int halfSize, float step)
    {
        var verts = new List<VertexPositionColor>();
        var col = new Color(46, 46, 72, 128);
        for (int i = -halfSize; i <= halfSize; i++)
        {
            float f = i * step;
            verts.Add(new VertexPositionColor(new Vector3(f, 0, -halfSize * step), col));
            verts.Add(new VertexPositionColor(new Vector3(f, 0, halfSize * step), col));
            verts.Add(new VertexPositionColor(new Vector3(-halfSize * step, 0, f), col));
            verts.Add(new VertexPositionColor(new Vector3(halfSize * step, 0, f), col));
        }
        _gridVerts = verts.ToArray();
    }

    private void DrawGrid(Matrix view, Matrix projection)
    {
        if (_gridVerts == null || _gridEffect == null) return;
        _gridEffect.View = view;
        _gridEffect.Projection = projection;
        _gridEffect.World = Matrix.Identity;
        _gridEffect.VertexColorEnabled = true;
        _gridEffect.TextureEnabled = false;
        _gridEffect.LightingEnabled = false;
        GraphicsDevice.BlendState = BlendState.AlphaBlend;
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        foreach (var pass in _gridEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            GraphicsDevice.DrawUserPrimitives(
                PrimitiveType.LineList, _gridVerts, 0, _gridVerts.Length / 2);
        }
    }

    // ── Camera auto-fit ───────────────────────────────────────────────────
    private void AutoFitCamera(C3Role role)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        bool any = false;

        // 1. Calculate boundaries strictly on the Body (ignores weapons/capes so camera doesn't zoom too far out)
        if (role.Body != null)
        {
            foreach (var phy in role.Body.Model.Phys)
            {
                if (phy.OutputVertices.Count == 0) continue;
                foreach (var v in phy.OutputVertices)
                {
                    min = Vector3.Min(min, v.Position);
                    max = Vector3.Max(max, v.Position);
                    any = true;
                }
            }
        }

        // 2. Fallback: fit to any attachment if no body vertices exist
        if (!any)
        {
            foreach (var part in role.AllParts())
            {
                foreach (var phy in part.Model.Phys)
                {
                    foreach (var v in phy.OutputVertices)
                    {
                        min = Vector3.Min(min, v.Position);
                        max = Vector3.Max(max, v.Position);
                        any = true;
                    }
                }
            }
        }

        if (!any) { _camera.Reset(); return; }

        var center = (min + max) * 0.5f;
        float diagonal = Vector3.Distance(min, max);
        float orbit = Math.Clamp(diagonal * 1.5f, 40f, 800f);
        _camera.FitTo(center, orbit);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────
    protected override void UnloadContent()
    {
        _renderer?.Dispose();
        _gridEffect?.Dispose();
        C3Texture.Texture_UnloadAll();
        base.UnloadContent();
    }
}