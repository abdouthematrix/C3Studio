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
        new VertexElement(0,  VertexElementFormat.Vector3, VertexElementUsage.Position,          0),
        new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal,            0),
        new VertexElement(24, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(32, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1));
    public const int SizeInBytes = 40;
}

/// <summary>
/// Static scene mesh with optional lightmap and per-frame animation.
/// Renders with D3DCULL_CW → CullCounterClockwise, z-write ON.
/// Lightmap pass approximated as additive second draw.
/// </summary>
public class C3Scene : IDisposable
{
    public string  Name         { get; set; } = string.Empty;
    public string  TexName      { get; set; } = string.Empty;
    public string? LightTexName { get; set; }
    public int     TexIndex     { get; set; } = -1;
    public int     LightTexIndex{ get; set; } = -1;

    public SceneVertex[]? Vertices { get; set; }
    public ushort[]?      Indices  { get; set; }
    public Matrix[]?      Frames   { get; set; }

    public int   CurrentFrame { get; set; }
    public Matrix ExtraMatrix { get; set; } = Matrix.Identity;

    private VertexBuffer? _vb;
    private IndexBuffer?  _ib;
    private Texture2D?    _tex;
    private Texture2D?    _lightTex;

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

      //  if (gd != null) scene.UploadGPU(gd);
        return scene;
    }

    public void UploadGPU(GraphicsDevice gd)
    {
        _vb?.Dispose(); _ib?.Dispose();
        if (Vertices == null || Vertices.Length == 0) return;
        _vb = new VertexBuffer(gd, SceneVertex.VertexDeclaration, Vertices.Length, BufferUsage.WriteOnly);
        _vb.SetData(Vertices);
        _ib = new IndexBuffer(gd, IndexElementSize.SixteenBits, Indices!.Length, BufferUsage.WriteOnly);
        _ib.SetData(Indices);
        _tex      = C3Texture.Get(TexIndex)?.Texture;
        _lightTex = C3Texture.Get(LightTexIndex)?.Texture;
    }

    public void NextFrame(int step=1)
    { if(Frames!=null&&Frames.Length>0) CurrentFrame=(CurrentFrame+step)%Frames.Length; }

    public void Draw(GraphicsDevice gd, BasicEffect effect, Matrix view, Matrix projection)
    {
        if (_vb == null || Indices == null) return;
        Matrix frameMatrix = (Frames != null && Frames.Length > 0) ? Frames[CurrentFrame] : Matrix.Identity;
        Matrix world = frameMatrix * ExtraMatrix;
        bool hasAlpha = _tex != null && (_tex.Format == SurfaceFormat.Dxt3 || _tex.Format == SurfaceFormat.Dxt5);
        gd.DepthStencilState = DepthStencilState.Default;
        gd.RasterizerState   = RasterizerState.CullCounterClockwise;
        gd.BlendState        = hasAlpha ? BlendState.AlphaBlend : BlendState.Opaque;
        gd.SamplerStates[0]  = SamplerState.LinearWrap;
        effect.View=view; effect.Projection=projection; effect.World=world;
        effect.TextureEnabled=_tex!=null; effect.Texture=_tex;
        effect.LightingEnabled=false; effect.VertexColorEnabled=false;
        gd.SetVertexBuffer(_vb); gd.Indices=_ib;
        foreach (var pass in effect.CurrentTechnique.Passes)
        { pass.Apply(); gd.DrawIndexedPrimitives(PrimitiveType.TriangleList,0,0,Indices.Length/3); }
        if (_lightTex != null)
        {
            gd.BlendState=BlendState.Additive; effect.Texture=_lightTex;
            foreach (var pass in effect.CurrentTechnique.Passes)
            { pass.Apply(); gd.DrawIndexedPrimitives(PrimitiveType.TriangleList,0,0,Indices.Length/3); }
            gd.BlendState=BlendState.Opaque;
        }
        ExtraMatrix = Matrix.Identity;
    }

    public void Dispose()
    {
        _vb?.Dispose(); _ib?.Dispose();
        if (TexIndex      != -1) { C3Texture.Texture_Unload(TexIndex);      TexIndex      = -1; }
        if (LightTexIndex != -1) { C3Texture.Texture_Unload(LightTexIndex); LightTexIndex = -1; }
    }

   
}
