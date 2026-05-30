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

/// <summary>
/// MonoGame WpfInterop game host.
/// Responsibilities: XNA lifecycle, camera input, grid rendering, playback control.
/// Asset loading is fully delegated to <see cref="C3AssetLoader"/>.
/// </summary>
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
    public IEnumerable<string> GetMeshNames() => _renderer?.GetPhyNames() ?? Array.Empty<string>();
    public bool GetMeshVisibility(string name) => _renderer?.GetPhyVisibility(name) ?? true;
    public void SetMeshVisibility(string name, bool visible) => _renderer?.SetPhyVisibility(name, visible);

    // ── Playback ──────────────────────────────────────────────────────────
    public bool IsPlaying { get; set; } = true;
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

    // ── Init ──────────────────────────────────────────────────────────────
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
            if (IsPlaying) _renderer.Update(gameTime);

            int total = _renderer.Model?.MaxFrameCount ?? 0;
            int current = 0;
            var model = _renderer.Model;
            if (model != null)
            {
                // Prefer physics motions
                if (model.Motions.Count > 0)
                    current = model.Motions[0].CurrentFrame;

                // Otherwise check shape motions
                else if (model.Shapes.Count > 0 && model.Shapes[0].Motion != null)
                    current = model.Shapes[0].Motion!.CurrentFrame;

                // Otherwise check particles
                else if (model.Ptcls.Count > 0)
                    current = model.Ptcls[0].CurrentFrame;

                // Otherwise check scenes
                else if (model.Scenes.Count > 0)
                    current = model.Scenes[0].CurrentFrame;
            }
            FrameChanged?.Invoke(current, total);
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

    /// <summary>
    /// Loads and merges multiple (mesh, texture, asb, adb) tuples into one model.
    /// Used for multi-part NPCs / SimpleObjs / equipment that carry per-slot blend info.
    /// </summary>
    public void LoadC3Parts(
        IEnumerable<(string MeshPath, string? TexturePath, int Asb, int Adb)> parts,
        string? motionPath = null)
    {
        if (_renderer == null || _loader == null) return;
        try
        {
            _renderer.Unload();
            var (model, partCount) = _loader.LoadAndMerge(parts);
            if (model == null) return;

            if (!string.IsNullOrEmpty(motionPath))
                ChangeMotion(motionPath);

            _renderer.LoadModelDirect(model);
            AutoFitCamera(model);
            ModelLoaded?.Invoke();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[C3StudioGame] LoadC3Parts: {ex.Message}");
        }
    }
    /// <summary>Swaps the animation on the current model.</summary>
    public void ChangeMotion(string relativePath)
    {
        if (_renderer == null || _loader == null) return;
        try
        {
            if (_assetService != null)
            {
                using var stream = _assetService.Open(relativePath);
                _renderer.ChangeMotion(stream);
            }            
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[C3StudioGame] ChangeMotion '{relativePath}': {ex.Message}");
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
    private void AutoFitCamera(C3Model model)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        bool any = false;

        foreach (var phy in model.Phys)
        {
            if (phy.PartIndex != 0) continue;
            if (phy.OutputVertices.Count == 0) continue;
            foreach (var v in phy.OutputVertices)
            {
                min = Vector3.Min(min, v.Position);
                max = Vector3.Max(max, v.Position);
                any = true;
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