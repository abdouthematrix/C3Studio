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

    // ── Bone Visibility Options ───────────────────────────────────────────
    private bool _showBones = false;
    public bool ShowBones
    {
        get => _showBones;
        set => _showBones = value;
    }
    private VertexPositionColor[] _axisVerts;
    private VertexPositionColor[] _bboxVerts;
    private List<VertexPositionColor> _boneVertices = new();
    public bool ShowBoundingBox { get; set; }
    public bool ShowAxisGizmo { get; set; }
    public bool IsOrthographic
    {
        get => _camera != null && _camera.IsOrthographic;
        set { if (_camera != null) _camera.IsOrthographic = value; }
    }

    // ── XNA services ──────────────────────────────────────────────────────
    private IGraphicsDeviceService? _gdService;
    private C3Renderer? _renderer;
    private C3AssetLoader? _loader;
    private BasicEffect? _gridEffect;
    private BasicEffect? _boneEffect; // Dedicated bone overlay effect
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

        _boneEffect = new BasicEffect(GraphicsDevice)
        {
            VertexColorEnabled = true,
            LightingEnabled = false,
            TextureEnabled = false,
        };

        _axisVerts = new VertexPositionColor[]
       {
            new VertexPositionColor(Vector3.Zero, Color.Red),   new VertexPositionColor(new Vector3(30f, 0f, 0f), Color.Red),
            new VertexPositionColor(Vector3.Zero, Color.Green), new VertexPositionColor(new Vector3(0f, 30f, 0f), Color.Green),
            new VertexPositionColor(Vector3.Zero, Color.Blue),  new VertexPositionColor(new Vector3(0f, 0f, 30f), Color.Blue)
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

        // Render bones on top of meshes if enabled
        if (_showBones)
        {
            DrawBones(view, projection);
        }
        if (ShowAxisGizmo) DrawAxisGizmo(view, projection);
        if (ShowBoundingBox) DrawBoundingBox(view, projection);

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

    public ulong SetAction(RoleActionType actionType)
    {
        if (_renderer == null) return 0;
        try
        {
            return _renderer.SetAction(actionType);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[C3StudioGame] ChangeMotion '{actionType}': {ex.Message}");
            return 0;
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

    private void DrawAxisGizmo(Matrix view, Matrix projection)
    {
        if (_axisVerts == null || _gridEffect == null) return;

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
            GraphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, _axisVerts, 0, _axisVerts.Length / 2);
        }
    }
    private void DrawBoundingBox(Matrix view, Matrix projection)
    {
        if (_bboxVerts == null || _gridEffect == null) return;

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
            GraphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, _bboxVerts, 0, _bboxVerts.Length / 2);
        }
    }

    // ── Bone Calculation & Primitives Pass ───────────────────────────────
    private void DrawBones(Matrix view, Matrix projection)
    {
        // Updated property checks based on the modern API
        if (_renderer?.Role?.Body?.Model?.Phys == null || _boneEffect == null)
            return;

        _boneVertices.Clear();
        Matrix worldTransform = Matrix.Identity; // Standard reference floor transformation

        foreach (var c3Phy in _renderer.Role.Body.Model.Phys)
        {
            if (c3Phy?.Motion == null)
                continue;

            int boneCount = c3Phy.Motion.BoneCount;
            BoneData[] bones = new BoneData[boneCount];

            // 1. Calculate transforms and flip Z to match the mesh exactly
            for (int b = 0; b < boneCount; b++)
            {
                // Replicate Calculate() exact bone matrix formula
                Matrix rawBone = c3Phy.InitMatrix * c3Phy.Motion.GetBoneMatrix(b) * c3Phy.Motion.BoneMatrix[b];

                // Extract the unflipped position and orientation vectors
                Vector3 pos = rawBone.Translation;
                Vector3 dir = rawBone.Up;
                Vector3 right = rawBone.Right;
                Vector3 forward = rawBone.Forward;

                // Apply the D3D left-hand → MonoGame right-hand mirror explicitly
                pos.Z = -pos.Z;
                dir.Z = -dir.Z;
                right.Z = -right.Z;
                forward.Z = -forward.Z;

                bones[b] = new BoneData
                {
                    Position = Vector3.Transform(pos, worldTransform),
                    Direction = Vector3.TransformNormal(dir, worldTransform),
                    Right = Vector3.TransformNormal(right, worldTransform),
                    Forward = Vector3.TransformNormal(forward, worldTransform),
                    Length = 1.0f
                };
            }

            // 2. Estimate length variants matching proximity boundaries
            for (int b = 0; b < boneCount; b++)
            {
                float minChildDist = float.MaxValue;
                bool hasChild = false;

                for (int c = 0; c < boneCount; c++)
                {
                    if (c != b)
                    {
                        float dist = Vector3.Distance(bones[b].Position, bones[c].Position);
                        if (dist > 0.01f && dist < minChildDist)
                        {
                            minChildDist = dist;
                            hasChild = true;
                        }
                    }
                }

                bones[b].Length = hasChild ? minChildDist * 0.5f : 2.0f;
            }

            // 3. Build volumetric primitives
            Color boneColor = new Color(100, 150, 255);
            Color boneOutline = new Color(50, 100, 200);

            for (int b = 0; b < boneCount; b++)
            {
                DrawOctahedralBone(bones[b], boneColor, boneOutline);
            }

            // 4. Generate skeletal linking wires
            Color connectionColor = Color.White * 0.5f;
            for (int b = 1; b < boneCount; b++)
            {
                float minDist = float.MaxValue;
                int parentIdx = 0;

                for (int p = 0; p < b; p++)
                {
                    float dist = Vector3.Distance(bones[b].Position, bones[p].Position);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        parentIdx = p;
                    }
                }

                if (minDist < 10.0f)
                {
                    _boneVertices.Add(new VertexPositionColor(bones[b].Position, connectionColor));
                    _boneVertices.Add(new VertexPositionColor(bones[parentIdx].Position, connectionColor));
                }
            }
        }

        if (_boneVertices.Count == 0)
            return;

        // Apply matrix state transformations
        _boneEffect.View = view;
        _boneEffect.Projection = projection;
        _boneEffect.World = Matrix.Identity;

        GraphicsDevice.BlendState = BlendState.AlphaBlend;
        // DepthStencilState.None allows bone viewing entirely through the character mesh
        GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;

        foreach (var pass in _boneEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            GraphicsDevice.DrawUserPrimitives(
                PrimitiveType.LineList, _boneVertices.ToArray(), 0, _boneVertices.Count / 2);
        }
    }

    private void DrawOctahedralBone(BoneData bone, Color fillColor, Color outlineColor)
    {
        Vector3 head = bone.Position;

        // Primary bone direction (usually Y/Up in 3D animation space)
        Vector3 boneDir = bone.Direction;
        if (boneDir.LengthSquared() > 0.001f)
            boneDir.Normalize();
        else
            boneDir = Vector3.Up;

        Vector3 tail = head + boneDir * bone.Length;

        // Local right axis safely extracted from the mirror
        Vector3 right = bone.Right;
        if (right.LengthSquared() > 0.001f)
            right.Normalize();
        else
            right = Vector3.Right;

        // Reconstruct forward using standard right-handed cross product
        Vector3 forward = Vector3.Cross(right, boneDir);
        if (forward.LengthSquared() > 0.001f)
            forward.Normalize();
        else
            forward = Vector3.Forward;

        // Re-cross right to ensure the octahedral base is perfectly square
        right = Vector3.Cross(boneDir, forward);

        float width = bone.Length * 0.15f;
        Vector3 mid = (head + tail) * 0.5f;

        Vector3[] verts = new Vector3[6]
        {
        head,
        mid + right * width,
        mid + forward * width,
        mid - right * width,
        mid - forward * width,
        tail
        };

        int[][] faces = new int[][]
        {
        new int[] {0, 1, 2}, new int[] {0, 2, 3},
        new int[] {0, 3, 4}, new int[] {0, 4, 1},
        new int[] {5, 2, 1}, new int[] {5, 3, 2},
        new int[] {5, 4, 3}, new int[] {5, 1, 4}
        };

        foreach (var face in faces)
        {
            _boneVertices.Add(new VertexPositionColor(verts[face[0]], fillColor));
            _boneVertices.Add(new VertexPositionColor(verts[face[1]], fillColor));

            _boneVertices.Add(new VertexPositionColor(verts[face[1]], fillColor));
            _boneVertices.Add(new VertexPositionColor(verts[face[2]], fillColor));

            _boneVertices.Add(new VertexPositionColor(verts[face[2]], fillColor));
            _boneVertices.Add(new VertexPositionColor(verts[face[0]], fillColor));
        }

        int[][] edges = new int[][]
        {
        new int[] {0, 1}, new int[] {0, 2}, new int[] {0, 3}, new int[] {0, 4},
        new int[] {1, 2}, new int[] {2, 3}, new int[] {3, 4}, new int[] {4, 1},
        new int[] {5, 1}, new int[] {5, 2}, new int[] {5, 3}, new int[] {5, 4}
        };

        foreach (var edge in edges)
        {
            _boneVertices.Add(new VertexPositionColor(verts[edge[0]], outlineColor));
            _boneVertices.Add(new VertexPositionColor(verts[edge[1]], outlineColor));
        }
    }

    private struct BoneData
    {
        public Vector3 Position { get; set; }
        public Vector3 Direction { get; set; }  // The bone's primary axis (Up)
        public Vector3 Right { get; set; }      // Orthogonal axis
        public Vector3 Forward { get; set; }    // Orthogonal axis
        public float Length { get; set; }
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

        // Rebuild wireframe bounding box corners (12 lines / 24 vertices)
        Color bboxColor = Color.Yellow; // Choose your preferred debug color
        _bboxVerts = new VertexPositionColor[]
        {  
            // Bottom face loops
            new VertexPositionColor(new Vector3(min.X, min.Y, min.Z), bboxColor), new VertexPositionColor(new Vector3(max.X, min.Y, min.Z), bboxColor),
            new VertexPositionColor(new Vector3(max.X, min.Y, min.Z), bboxColor), new VertexPositionColor(new Vector3(max.X, max.Y, min.Z), bboxColor),
            new VertexPositionColor(new Vector3(max.X, max.Y, min.Z), bboxColor), new VertexPositionColor(new Vector3(min.X, max.Y, min.Z), bboxColor),
            new VertexPositionColor(new Vector3(min.X, max.Y, min.Z), bboxColor), new VertexPositionColor(new Vector3(min.X, min.Y, min.Z), bboxColor),
            
            // Top face loops
            
            new VertexPositionColor(new Vector3(min.X, min.Y, max.Z), bboxColor), new VertexPositionColor(new Vector3(max.X, min.Y, max.Z), bboxColor),
            new VertexPositionColor(new Vector3(max.X, min.Y, max.Z), bboxColor), new VertexPositionColor(new Vector3(max.X, max.Y, max.Z), bboxColor),
            new VertexPositionColor(new Vector3(max.X, max.Y, max.Z), bboxColor), new VertexPositionColor(new Vector3(min.X, max.Y, max.Z), bboxColor),
            new VertexPositionColor(new Vector3(min.X, max.Y, max.Z), bboxColor), new VertexPositionColor(new Vector3(min.X, min.Y, max.Z), bboxColor),
            
            // Vertical pillars
            
            new VertexPositionColor(new Vector3(min.X, min.Y, min.Z), bboxColor), new VertexPositionColor(new Vector3(min.X, min.Y, max.Z), bboxColor),
            new VertexPositionColor(new Vector3(max.X, min.Y, min.Z), bboxColor), new VertexPositionColor(new Vector3(max.X, min.Y, max.Z), bboxColor),
            new VertexPositionColor(new Vector3(max.X, max.Y, min.Z), bboxColor), new VertexPositionColor(new Vector3(max.X, max.Y, max.Z), bboxColor),
            new VertexPositionColor(new Vector3(min.X, max.Y, min.Z), bboxColor), new VertexPositionColor(new Vector3(min.X, max.Y, max.Z), bboxColor)
        };

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
        _boneEffect?.Dispose();
        C3Texture.Texture_UnloadAll();
        base.UnloadContent();
    }
}