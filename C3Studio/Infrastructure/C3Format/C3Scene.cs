using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace C3Studio.Infrastructure.C3Format;

public struct SceneVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 UV0;
    public Vector2 UV1;

    public static readonly VertexDeclaration VertexDeclaration = new(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
        new VertexElement(24, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(32, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1));
    public const int SizeInBytes = 40;
}

/// <summary>
/// Static scene mesh with optional lightmap and per-frame matrix animation (SCEN chunk).
///
/// Each instance owns an <see cref="AlphaTestEffect"/> created in <see cref="UploadGPU"/>.
/// Renders with D3DCULL_CW → CullCounterClockwise, z-write ON.
/// Lightmap pass approximated as a second additive draw.
/// Blend state is resolved via <see cref="C3BlendHelper"/>.
/// </summary>
public class C3Scene : IDisposable
{
    

    // D3D blend factors: 5=SrcAlpha, 6=InvSrcAlpha (standard AlphaBlend)
    public int BlendAsb { get; set; } = 5;
    public int BlendAdb { get; set; } = 6;

    public string Name { get; set; } = string.Empty;
    public string TexName { get; set; } = string.Empty;
    public string? LightTexName { get; set; }
    public int TexIndex { get; set; } = -1;
    public int LightTexIndex { get; set; } = -1;

    public SceneVertex[]? Vertices { get; set; }
    public ushort[]? Indices { get; set; }
    public Matrix[]? Frames { get; set; }

    public int CurrentFrame { get; set; }
    public Matrix ExtraMatrix { get; set; } = Matrix.Identity;

    private VertexBuffer? _vb;
    private IndexBuffer? _ib;
    private Texture2D? _tex;
    private Texture2D? _lightTex;

    // Per-instance effect — created in UploadGPU.
    private AlphaTestEffect? _effect;

    // ── Serialisation ─────────────────────────────────────────────────────
    public static C3Scene Load(BinaryReader br)
    {
        var scene = new C3Scene();

        uint nLen = br.ReadUInt32();
        scene.Name = Encoding.ASCII.GetString(br.ReadBytes((int)nLen)).TrimEnd('\0');

        uint vecCount = br.ReadUInt32();
        scene.Vertices = new SceneVertex[vecCount];
        for (int i = 0; i < (int)vecCount; i++)
        {
            scene.Vertices[i].Position = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            scene.Vertices[i].Normal = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            scene.Vertices[i].UV0 = new Vector2(br.ReadSingle(), br.ReadSingle());
            scene.Vertices[i].UV1 = new Vector2(br.ReadSingle(), br.ReadSingle());
        }

        uint triCount = br.ReadUInt32();
        scene.Indices = new ushort[triCount * 3];
        for (int i = 0; i < (int)(triCount * 3); i++) scene.Indices[i] = br.ReadUInt16();

        uint texLen = br.ReadUInt32();
        scene.TexName = Encoding.ASCII.GetString(br.ReadBytes((int)texLen)).TrimEnd('\0');

        uint lmLen = br.ReadUInt32();
        if (lmLen > 0)
            scene.LightTexName = Encoding.ASCII.GetString(br.ReadBytes((int)lmLen)).TrimEnd('\0');

        uint fc = br.ReadUInt32();
        scene.Frames = new Matrix[fc];
        for (int i = 0; i < (int)fc; i++) scene.Frames[i] = C3Motion.ReadMatrix(br);

        return scene;
    }

    // ── GPU lifecycle ─────────────────────────────────────────────────────
    /// <summary>
    /// Uploads geometry to the GPU and creates the private effect.
    /// Call once on the render thread after Load().
    /// </summary>
    public void UploadGPU(GraphicsDevice gd)
    {
        _vb?.Dispose(); _ib?.Dispose();
        if (Vertices == null || Vertices.Length == 0) return;

        _vb = new VertexBuffer(gd, SceneVertex.VertexDeclaration, Vertices.Length, BufferUsage.WriteOnly);
        _vb.SetData(Vertices);

        _ib = new IndexBuffer(gd, IndexElementSize.SixteenBits, Indices!.Length, BufferUsage.WriteOnly);
        _ib.SetData(Indices);

        _tex = C3Texture.Get(TexIndex)?.Texture;
        _lightTex = C3Texture.Get(LightTexIndex)?.Texture;

        _effect?.Dispose();
        _effect = new AlphaTestEffect(gd)
        {
            AlphaFunction = CompareFunction.GreaterEqual,
            ReferenceAlpha = 8,
        };
    }

    // ── Animation control ─────────────────────────────────────────────────
    public void NextFrame(int step = 1)
    {
        if (Frames != null && Frames.Length > 0)
            CurrentFrame = (CurrentFrame + step) % Frames.Length;
    }

    // ── Rendering ─────────────────────────────────────────────────────────
    /// <summary>
    /// Draws the scene mesh using its private <see cref="AlphaTestEffect"/>.
    /// Blend state is derived from the texture format; the lightmap pass is
    /// drawn additively as a second call.
    /// </summary>
    public void Draw(GraphicsDevice gd, Matrix view, Matrix projection)
    {
        if (_vb == null || Indices == null || _effect == null) return;

        Matrix frameMatrix = (Frames != null && Frames.Length > 0) ? Frames[CurrentFrame] : Matrix.Identity;
        Matrix world = frameMatrix * ExtraMatrix;

        bool hasAlpha = _tex != null && (_tex.Format == SurfaceFormat.Dxt3 || _tex.Format == SurfaceFormat.Dxt5);

        gd.DepthStencilState = DepthStencilState.Default;
        gd.RasterizerState = RasterizerState.CullCounterClockwise;
        gd.BlendState = hasAlpha ? C3BlendHelper.Resolve(BlendAsb, BlendAdb) : BlendState.Opaque;
        gd.SamplerStates[0] = SamplerState.LinearWrap;

        _effect.View = view;
        _effect.Projection = projection;
        _effect.World = world;
        _effect.Texture = _tex;
        _effect.VertexColorEnabled = false;

        gd.SetVertexBuffer(_vb);
        gd.Indices = _ib;
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, Indices.Length / 3);
        }

        // Second pass: additive lightmap
        if (_lightTex != null)
        {
            gd.BlendState = BlendState.Additive;
            _effect.Texture = _lightTex;
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, Indices.Length / 3);
            }
            gd.BlendState = BlendState.Opaque;
        }

        ExtraMatrix = Matrix.Identity;
    }

    // ── IDisposable ───────────────────────────────────────────────────────
    public void Dispose()
    {
        _vb?.Dispose(); _vb = null;
        _ib?.Dispose(); _ib = null;
        _effect?.Dispose(); _effect = null;

        if (TexIndex != -1) { C3Texture.Texture_Unload(TexIndex); TexIndex = -1; }
        if (LightTexIndex != -1) { C3Texture.Texture_Unload(LightTexIndex); LightTexIndex = -1; }
    }
}