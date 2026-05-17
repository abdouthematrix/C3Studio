using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace C3Studio.Infrastructure.C3Format;

public static class C3Constants { public const int BoneMax = 2; public const int MorphMax = 4; }

public class PhyVertex
{
    public Vector3[] Positions;
    public Vector2 TexCoord;
    public int[] BoneIndex;
    public float[] BoneWeight;

    public PhyVertex(int morphMax)
    {
        Positions = new Vector3[morphMax];
        BoneIndex = new int[C3Constants.BoneMax];
        BoneWeight = new float[C3Constants.BoneMax];
    }
}

public class PhyOutVertex
{
    public Vector3 Position = Vector3.Zero;
    public Vector2 TexCoord = Vector2.Zero;
}

/// <summary>
/// One skinned mesh inside a .c3 file.
///
/// GPU lifecycle:
///   Call <see cref="InitializeGPU"/> once after loading to create vertex/index
///   buffers and the mesh's private effects.  Call <see cref="UploadVertices"/>
///   every frame after <see cref="Calculate"/> has updated <see cref="OutputVertices"/>.
///   Call <see cref="Rebuild"/> after a PHY slot is hot-swapped at runtime.
///   Dispose the instance to release all GPU resources.
///
/// Rendering:
///   <see cref="DrawNormal"/> → opaque triangles (BasicEffect, no blending).
///   <see cref="DrawAlpha"/>  → translucent/alpha triangles (AlphaTestEffect,
///   blend mode resolved from <see cref="BlendAsb"/>/<see cref="BlendAdb"/> via
///   <see cref="C3BlendHelper"/>).
///
/// Material tint (R,G,B,Alpha) is pushed into the effect's DiffuseColor/Alpha
/// each draw call, matching Phy_DrawNormal / Phy_DrawAlpha in the original C++.
/// Blend factors mirror D3D values: 5/6 = AlphaBlend, 2/2 = Additive.
/// </summary>
public class C3Phy : IDisposable
{
    public int PartIndex = -1;

    // ── Identity / material ───────────────────────────────────────────────
    public string Name { get; set; } = string.Empty;
    public string TexName { get; set; } = string.Empty;
    public int TexIndex { get; set; } = -1;
    /// <summary>Secondary texture slot — mirrors <c>nTex2</c> in the C++ struct.</summary>
    public int TexIndex2 { get; set; } = 0;

    // ── Geometry counts ───────────────────────────────────────────────────
    public int NormalVertexCount { get; set; }
    public int AlphaVertexCount { get; set; }
    public int NormalTriCount { get; set; }
    public int AlphaTriCount { get; set; }
    public int BlendCount { get; set; }

    // ── CPU-side buffers (kept for skinning / re-upload) ──────────────────
    public List<PhyVertex> SourceVertices { get; } = new();
    public List<PhyOutVertex> OutputVertices { get; } = new();
    public List<ushort> IndexBuffer { get; } = new();

    // ── Bounding / transform ──────────────────────────────────────────────
    public Vector3 BBoxMin { get; set; }
    public Vector3 BBoxMax { get; set; }
    public Matrix InitMatrix { get; set; } = Matrix.Identity;
    public C3Motion? Motion { get; set; }
    public C3Key Key { get; set; } = new();

    // ── Material ──────────────────────────────────────────────────────────
    public float Alpha { get; set; } = 1f;
    public float R { get; set; } = 1f;
    public float G { get; set; } = 1f;
    public float B { get; set; } = 1f;
    public bool Draw { get; set; } = true;

    // D3D blend factors: 5=SrcAlpha, 6=InvSrcAlpha (standard AlphaBlend)
    public int BlendAsb { get; set; } = 5;
    public int BlendAdb { get; set; } = 6;

    public int TexRow { get; set; } = 1;
    public Vector2 UVStep { get; set; } = Vector2.Zero;
    private Vector2 _accumUV;

    public bool TwoSided { get; set; }
    public bool IsFullyOpaque => Alpha == 1f;

    // ── GPU resources (owned by this instance after InitializeGPU) ────────
    private DynamicVertexBuffer? _vertexBuffer;
    private IndexBuffer? _indexBuffer;

    /// <summary>
    /// Resolved render texture for this mesh.
    /// Set by the renderer after loading (may differ from the embedded TexIndex).
    /// </summary>
    public Texture2D? GpuTexture { get; set; }

    // ── Per-instance effects ──────────────────────────────────────────────
    /// <summary>BasicEffect used for the fully-opaque draw pass.</summary>
    private BasicEffect? _basicEffect;
    /// <summary>AlphaTestEffect used for the translucent / alpha-tri draw pass.</summary>
    private AlphaTestEffect? _alphaTestEffect;

    // ── Derived helpers ───────────────────────────────────────────────────
    public int TotalVertexCount => NormalVertexCount + AlphaVertexCount;
    public int TotalIndexCount => (NormalTriCount + AlphaTriCount) * 3;
    public int AlphaIndexStart => NormalTriCount * 3;

    // ── Serialisation ─────────────────────────────────────────────────────
    public static C3Phy Load(BinaryReader br, string chunkTag)
    {
        var phy = new C3Phy();

        uint nameLen = br.ReadUInt32();
        phy.Name = Encoding.ASCII.GetString(br.ReadBytes((int)nameLen)).TrimEnd('\0');

        phy.BlendCount = (int)br.ReadUInt32();
        phy.NormalVertexCount = (int)br.ReadUInt32();
        phy.AlphaVertexCount = (int)br.ReadUInt32();

        int totalVerts = phy.NormalVertexCount + phy.AlphaVertexCount;
        int morphMax = (chunkTag == "PHY3" || chunkTag == "PHY4") ? 1 : C3Constants.MorphMax;

        for (int i = 0; i < totalVerts; i++)
        {
            var v = new PhyVertex(morphMax);
            for (int m = 0; m < morphMax; m++)
                v.Positions[m] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            v.TexCoord = new Vector2(br.ReadSingle(), br.ReadSingle());
            br.ReadBytes(4); // vertex diffuse (always white)
            for (int b = 0; b < C3Constants.BoneMax; b++) v.BoneIndex[b] = (int)br.ReadUInt32();
            for (int b = 0; b < C3Constants.BoneMax; b++) v.BoneWeight[b] = br.ReadSingle();
            if (chunkTag == "PHY3") br.ReadBytes(12); // normal
            phy.SourceVertices.Add(v);
            phy.OutputVertices.Add(new PhyOutVertex { Position = v.Positions[0], TexCoord = v.TexCoord });
        }

        phy.NormalTriCount = (int)br.ReadUInt32();
        phy.AlphaTriCount = (int)br.ReadUInt32();
        int totalIdx = (phy.NormalTriCount + phy.AlphaTriCount) * 3;
        for (int i = 0; i < totalIdx; i++) phy.IndexBuffer.Add(br.ReadUInt16());

        uint texLen = br.ReadUInt32();
        byte[] txb = br.ReadBytes((int)texLen);
        try { phy.TexName = Encoding.GetEncoding("GBK").GetString(txb).TrimEnd('\0'); }
        catch { phy.TexName = Encoding.ASCII.GetString(txb).TrimEnd('\0'); }

        phy.BBoxMin = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        phy.BBoxMax = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        phy.InitMatrix = C3Motion.ReadMatrix(br);
        phy.TexRow = (int)br.ReadUInt32();

        int ac = (int)br.ReadUInt32(); for (int i = 0; i < ac; i++) phy.Key.Alphas.Add(C3Frame.Read(br));
        int dc = (int)br.ReadUInt32(); for (int i = 0; i < dc; i++) phy.Key.Draws.Add(C3Frame.Read(br));
        int cc = (int)br.ReadUInt32(); for (int i = 0; i < cc; i++) phy.Key.ChangeTexs.Add(C3Frame.Read(br));

        byte[] f1 = br.ReadBytes(4);
        if (Encoding.ASCII.GetString(f1) == "STEP")
            phy.UVStep = new Vector2(br.ReadSingle(), br.ReadSingle());
        else br.BaseStream.Seek(-4, SeekOrigin.Current);

        byte[] f2 = br.ReadBytes(4);
        if (Encoding.ASCII.GetString(f2) == "2SID") phy.TwoSided = true;
        else br.BaseStream.Seek(-4, SeekOrigin.Current);

        return phy;
    }

    // ── GPU lifecycle ─────────────────────────────────────────────────────
    /// <summary>
    /// Creates GPU vertex/index buffers and the mesh's private effects.
    /// Must be called once on the render thread before any Draw call.
    /// Safe to call again — disposes previous resources first.
    /// </summary>
    public void InitializeGPU(GraphicsDevice gd)
    {
        // Effects ─────────────────────────────────────────────────────────
        _basicEffect?.Dispose();
        _basicEffect = new BasicEffect(gd)
        {
            LightingEnabled = false,
            VertexColorEnabled = true,
            TextureEnabled = true,
        };

        _alphaTestEffect?.Dispose();
        _alphaTestEffect = new AlphaTestEffect(gd)
        {
            AlphaFunction = CompareFunction.GreaterEqual,
            ReferenceAlpha = 8,
        };

        // GPU buffers ─────────────────────────────────────────────────────
        RebuildBuffers(gd);
    }

    /// <summary>
    /// Rebuilds GPU buffers after a PHY slot hot-swap.
    /// Effects are preserved; only the vertex/index buffers are replaced.
    /// </summary>
    public void Rebuild(GraphicsDevice gd)
    {
        if (_basicEffect == null) { InitializeGPU(gd); return; }
        RebuildBuffers(gd);
    }

    private void RebuildBuffers(GraphicsDevice gd)
    {
        _vertexBuffer?.Dispose(); _vertexBuffer = null;
        _indexBuffer?.Dispose(); _indexBuffer = null;

        if (TotalVertexCount == 0 || TotalIndexCount == 0) return;

        _vertexBuffer = new DynamicVertexBuffer(gd,
            VertexPositionColorTexture.VertexDeclaration,
            TotalVertexCount, BufferUsage.WriteOnly);

        _indexBuffer = new IndexBuffer(gd, IndexElementSize.SixteenBits,
            TotalIndexCount, BufferUsage.WriteOnly);
        _indexBuffer.SetData(IndexBuffer.ToArray());

        UploadVertices();
    }

    /// <summary>
    /// Uploads the current <see cref="OutputVertices"/> to the GPU.
    /// Call after <see cref="Calculate"/> each frame.
    /// </summary>
    public void UploadVertices()
    {
        if (_vertexBuffer == null) return;
        var verts = BuildGpuVertices();
        if (verts.Length > 0)
            _vertexBuffer.SetData(verts, 0, verts.Length, SetDataOptions.Discard);
    }

    // ── Draw calls ────────────────────────────────────────────────────────
    /// <summary>
    /// Draws fully-opaque triangles with z-write on.
    /// Mirrors <c>Phy_DrawNormal</c>: skips if alpha &lt; 1 or no normal tris.
    /// </summary>
    public void DrawNormal(GraphicsDevice gd, Matrix view, Matrix projection, Matrix world)
    {
        if (!Draw || NormalTriCount == 0 || !IsFullyOpaque || _vertexBuffer == null) return;

        gd.RasterizerState = TwoSided ? RasterizerState.CullNone : RasterizerState.CullClockwise;
        gd.BlendState = BlendState.Opaque;
        gd.DepthStencilState = DepthStencilState.Default;
        gd.SamplerStates[0] = SamplerState.LinearWrap;

        var fx = _basicEffect!;
        fx.View = view;
        fx.Projection = projection;
        fx.World = world;
        fx.TextureEnabled = GpuTexture != null;
        fx.Texture = GpuTexture;
        fx.DiffuseColor = new Vector3(R, G, B);
        fx.Alpha = Alpha;
        fx.VertexColorEnabled = true;
        fx.LightingEnabled = false;

        gd.SetVertexBuffer(_vertexBuffer);
        gd.Indices = _indexBuffer;
        foreach (var pass in fx.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, NormalTriCount);
        }
    }

    /// <summary>
    /// Draws translucent/alpha triangles.
    /// Mirrors <c>Phy_DrawAlpha</c>: draws normal tris when alpha &lt; 1,
    /// and/or alpha tris when present.  Blend mode from D3D Asb/Adb values.
    /// <paramref name="bZ"/> mirrors the <c>bZ</c> parameter (D3DRS_ZWRITEENABLE);
    /// default is false, matching the C++ default.
    /// </summary>
    public void DrawAlpha(GraphicsDevice gd, Matrix view, Matrix projection, Matrix world, bool bZ = false)
    {
        if (!Draw || _vertexBuffer == null) return;
        bool tn = NormalTriCount > 0 && !IsFullyOpaque;
        bool at = AlphaTriCount > 0;
        if (!tn && !at) return;

        gd.RasterizerState = TwoSided ? RasterizerState.CullNone : RasterizerState.CullClockwise;
        gd.BlendState = C3BlendHelper.Resolve(BlendAsb, BlendAdb);
        gd.DepthStencilState = bZ ? DepthStencilState.Default : DepthStencilState.DepthRead;
        gd.SamplerStates[0] = SamplerState.LinearWrap;

        var fx = _alphaTestEffect!;
        fx.View = view;
        fx.Projection = projection;
        fx.World = world;
        fx.Texture = GpuTexture;
        fx.DiffuseColor = new Vector3(R, G, B);
        fx.Alpha = Alpha;
        fx.VertexColorEnabled = true;

        gd.SetVertexBuffer(_vertexBuffer);
        gd.Indices = _indexBuffer;
        foreach (var pass in fx.CurrentTechnique.Passes)
        {
            pass.Apply();
            if (tn) gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, NormalTriCount);
            if (at) gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, AlphaIndexStart, AlphaTriCount);
        }
    }

    // ── Phy_SetColor ──────────────────────────────────────────────────────
    /// <summary>
    /// Sets the material tint.  Mirrors <c>Phy_SetColor</c>.
    /// </summary>
    public void SetColor(float alpha, float red, float green, float blue)
    {
        Alpha = alpha;
        R = red;
        G = green;
        B = blue;
    }

    // ── Phy_ClearMatrix ───────────────────────────────────────────────────
    /// <summary>
    /// Resets every per-bone accumulation matrix to identity.
    /// Mirrors <c>Phy_ClearMatrix</c>, which resets <c>lpMotion->matrix[n]</c>.
    /// </summary>
    public void ClearMatrix()
    {
        if (Motion == null) return;
        for (int n = 0; n < Motion.BoneCount; n++)
            Motion.BoneMatrix[n] = Matrix.Identity;
    }

    // ── Phy_Muliply ───────────────────────────────────────────────────────
    /// <summary>
    /// Post-multiplies the accumulation matrix for the given bone (or all
    /// bones when <paramref name="boneIndex"/> is -1).
    /// Mirrors <c>Phy_Muliply</c> (note: original spelling kept).
    /// </summary>
    public void Multiply(int boneIndex, Matrix matrix)
    {
        if (Motion == null) return;
        int start = boneIndex == -1 ? 0 : boneIndex;
        int end = boneIndex == -1 ? Motion.BoneCount : boneIndex + 1;
        for (int n = start; n < end; n++)
            Motion.BoneMatrix[n] = Motion.BoneMatrix[n] * matrix;
    }

    // ── Phy_NextFrame ─────────────────────────────────────────────────────
    /// <summary>
    /// Advances the current animation frame by <paramref name="step"/>,
    /// wrapping at the total frame count.
    /// Mirrors <c>Phy_NextFrame</c>.
    /// </summary>
    public void NextFrame(int step)
    {
        if (Motion == null || Motion.FrameCount == 0) return;
        Motion.CurrentFrame = (Motion.CurrentFrame + step) % Motion.FrameCount;
    }

    // ── Phy_SetFrame ──────────────────────────────────────────────────────
    /// <summary>
    /// Seeks to an absolute frame, clamped by modulo.
    /// Mirrors <c>Phy_SetFrame</c>.
    /// </summary>
    public void SetFrame(int frame)
    {
        if (Motion == null) return;
        Motion.CurrentFrame = Motion.FrameCount == 0
            ? 0
            : frame % Motion.FrameCount;
    }

    // ── Phy_ChangeTexture ─────────────────────────────────────────────────
    /// <summary>
    /// Swaps the texture slot(s) used by this mesh at runtime.
    /// Mirrors <c>Phy_ChangeTexture</c>.
    /// </summary>
    public void ChangeTexture(int texId, int texId2 = 0)
    {
        TexIndex = texId;
        TexIndex2 = texId2;
    }

    // ── Skinning / CPU update ─────────────────────────────────────────────
    public void Calculate()
    {
        if (Motion == null) return;

        var (af, alpha) = Key.ProcessAlpha(Motion.CurrentFrame, Motion.FrameCount);
        if (af) Alpha = alpha;
        var (df, vis) = Key.ProcessDraw(Motion.CurrentFrame);
        if (df) Draw = vis;
        var (tf, tex) = Key.ProcessChangeTex(Motion.CurrentFrame);
        if (!tf) tex = -1;

        if (!Draw) return;

        var bone = new Matrix[Motion.BoneCount];
        for (int b = 0; b < Motion.BoneCount; b++)
            bone[b] = InitMatrix * Motion.GetBoneMatrix(b) * Motion.BoneMatrix[b];

        _accumUV += UVStep;
        float seg = TexRow > 0 ? 1f / TexRow : 1f;

        for (int v = 0; v < SourceVertices.Count; v++)
        {
            var sv = SourceVertices[v];
            Vector3 pos = Vector3.Zero;
            for (int l = 0; l < C3Constants.BoneMax; l++)
                if (sv.BoneWeight[l] > 0f)
                { pos = Vector3.Transform(sv.Positions[0], bone[sv.BoneIndex[l]]); break; }

            pos.Z = -pos.Z; // flip D3D left-hand → MonoGame right-hand
            OutputVertices[v].Position = pos;
            OutputVertices[v].TexCoord = tex > -1
                ? new Vector2(sv.TexCoord.X + (tex % TexRow) * seg, sv.TexCoord.Y + (tex / TexRow) * seg)
                : sv.TexCoord + _accumUV;
        }
    }

    public VertexPositionColorTexture[] BuildGpuVertices()
    {
        var verts = new VertexPositionColorTexture[OutputVertices.Count];
        for (int i = 0; i < OutputVertices.Count; i++)
            verts[i] = new VertexPositionColorTexture(
                OutputVertices[i].Position, Color.White, OutputVertices[i].TexCoord);
        return verts;
    }

    // ── IDisposable ───────────────────────────────────────────────────────
    public void Dispose()
    {
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _basicEffect?.Dispose();
        _alphaTestEffect?.Dispose();
        _vertexBuffer = null;
        _indexBuffer = null;
        _basicEffect = null;
        _alphaTestEffect = null;

        if (TexIndex != -1) { C3Texture.Texture_Unload(TexIndex); TexIndex = -1; }
        if (TexIndex2 != 0) { C3Texture.Texture_Unload(TexIndex2); TexIndex2 = 0; }
    }
}