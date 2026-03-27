using System.IO;
using C3Studio.Core.Services;
using C3Studio.Infrastructure.C3Format;
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
/// – Orbit / pan / zoom camera via <see cref="OrbitCamera"/>
/// – Drives C3Renderer
/// – Exposes IsPlaying, SetFps(), LoadC3(), StepFrame(), ResetCamera()
/// </summary>
public class C3StudioGame : WpfGame
{
    // ── DI ────────────────────────────────────────────────────────────────
    public IAssetFileService? AssetService { get; set; }

    // ── Events ────────────────────────────────────────────────────────────
    public event Action<int, int>? FrameChanged;

    // ── Playback ──────────────────────────────────────────────────────────
    public bool IsPlaying { get; set; } = true;

    public void SetFps(float fps) { if (_renderer != null) _renderer.Fps = fps; }
    public void StepFrame(int delta) => _renderer?.StepFrame(delta);
    public void ResetCamera() => _camera.Reset();

    // ── XNA services ──────────────────────────────────────────────────────
    private IGraphicsDeviceService? _gdService;
    private C3Renderer? _renderer;
    private BasicEffect? _gridEffect;
    private WpfMouse? _mouse;
    private WpfKeyboard? _keyboard;

    // ── Camera ────────────────────────────────────────────────────────────
    private static readonly Matrix WorldCorrection =
        Matrix.CreateRotationX(MathHelper.ToRadians(90f)) *
        Matrix.CreateRotationY(MathHelper.ToRadians(180f));

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

        _renderer = new C3Renderer(GraphicsDevice);
        _gridEffect = new BasicEffect(GraphicsDevice)
        {
            VertexColorEnabled = true,
            LightingEnabled = false,
            TextureEnabled = false,
        };

        BuildGrid(20, 50f);
    }

    // ── Update ────────────────────────────────────────────────────────────
    protected override void Update(GameTime gameTime)
    {
        HandleCamera();

        if (_renderer != null)
        {
            if (IsPlaying) _renderer.Update(gameTime);

            int total = _renderer.Model?.MaxFrameCount ?? 0;
            int cur = _renderer.Model?.Motions.Count > 0
                        ? _renderer.Model.Motions[0].CurrentFrame : 0;
            FrameChanged?.Invoke(cur, total);
        }

        base.Update(gameTime);
    }

    // ── Draw ──────────────────────────────────────────────────────────────
    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(11, 11, 20));

        float aspect = (float)Math.Max(1, GraphicsDevice.Viewport.Width)
                             / Math.Max(1, GraphicsDevice.Viewport.Height);

        var view = _camera.View;
        var projection = _camera.Projection(aspect);

        DrawGrid(view, projection);
        _renderer?.Draw(view, projection);

        base.Draw(gameTime);
    }

    // ── Camera input ──────────────────────────────────────────────────────
    private void HandleCamera()
    {
        var ms = _mouse!.GetState();
        var kb = _keyboard!.GetState();
        int dz = ms.ScrollWheelValue - _prevMouse.ScrollWheelValue;

        // Scroll zoom
        if (dz != 0)
            _camera.Zoom(dz * 0.01f);

        // W/S keyboard zoom
        if (kb.IsKeyDown(Keys.W)) _camera.Zoom(0.05f);
        if (kb.IsKeyDown(Keys.S)) _camera.Zoom(-0.05f);

        bool leftHeld = ms.LeftButton == ButtonState.Pressed
                      && _prevMouse.LeftButton == ButtonState.Pressed;
        bool rightHeld = ms.RightButton == ButtonState.Pressed
                      && _prevMouse.RightButton == ButtonState.Pressed;
        bool midHeld = ms.MiddleButton == ButtonState.Pressed
                      && _prevMouse.MiddleButton == ButtonState.Pressed;

        float dx = ms.X - _prevMouse.X;
        float dy = ms.Y - _prevMouse.Y;

        // Left-drag → orbit
        if (leftHeld)
            _camera.Orbit(dx * 0.005f, dy * 0.005f);

        // Right-drag or middle-drag → pan
        if (rightHeld || midHeld)
            _camera.Pan(dx, dy);

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

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Load from an absolute filesystem path.</summary>
    public void LoadC3(string path, string? texturePath = null)
    {
        _renderer?.LoadModel(path, texturePath: texturePath, worldRotation: WorldCorrection);
    }

    /// <summary>
    /// Load from a relative path via <see cref="IAssetFileService"/> (WDF-aware).
    /// Falls back gracefully to treating the path as absolute if AssetService is not set.
    /// </summary>
    public void LoadC3Asset(string relativePath, string? texturePath = null)
    {
        if (_renderer == null) return;

        try
        {
            if (AssetService != null)
            {
                using var stream = AssetService.Open(relativePath);
                LoadC3(stream, relativePath, texturePath);   // ← pass it on
            }
            else
            {
                _renderer.LoadModel(relativePath,
                    texturePath: texturePath,
                    worldRotation: WorldCorrection);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[C3StudioGame] LoadC3Asset '{relativePath}': {ex.Message}");
        }
    }

    /// <summary>
    /// Load from a stream (e.g. sourced from WDF via AssetService).
    /// sourceName is used for texture lookup.
    /// Auto-fits the camera to the loaded model's bounding radius.
    /// </summary>
    public void LoadC3(Stream stream, string sourceName, string? texturePath = null)
    {
        if (_renderer == null) return;

        var model = C3Model.LoadFromStream(stream, loadTextures: true, gd: GraphicsDevice);
        model.SourcePath = sourceName;
        _renderer.LoadModelDirect(model, worldRotation: WorldCorrection);

        // Override per-phy texture with an explicit file if supplied
        if (!string.IsNullOrEmpty(texturePath))
            _renderer.OverrideTexture(texturePath);          // ← see C3Renderer addition below

        AutoFitCamera(model);
    }
    public void ChangeMotion(string path)
    => _renderer?.ChangeMotion(path, WorldCorrection);

    // ── Camera auto-fit ───────────────────────────────────────────────────
    private void AutoFitCamera(C3Model model)
    {
        if (model.Phys.Count == 0) { _camera.Reset(100f); return; }

        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (var phy in model.Phys)
        {
            // Skip phys whose bounding boxes were never written (all-zero means no data)
            if (phy.BBoxMin == Vector3.Zero && phy.BBoxMax == Vector3.Zero) continue;
            min = Vector3.Min(min, phy.BBoxMin);
            max = Vector3.Max(max, phy.BBoxMax);
        }

        // Fallback: bbox was never populated, use a reasonable default
        if (min.X == float.MaxValue) { _camera.Reset(120f); return; }

        var center = Vector3.Transform((min + max) * 0.5f, WorldCorrection);
        var extent = Vector3.Distance(min, max) * 0.5f;
        _camera.Target = center;
        _camera.Radius = Math.Clamp(extent * 3f, 40f, 800f);
    }

    protected override void UnloadContent()
    {
        _renderer?.Dispose();
        _gridEffect?.Dispose();
        C3Texture.Texture_UnloadAll();
        base.UnloadContent();
    }
}